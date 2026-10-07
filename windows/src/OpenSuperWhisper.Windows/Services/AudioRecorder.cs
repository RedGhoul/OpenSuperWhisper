using NAudio.Wave;

namespace OpenSuperWhisper.Services;

public sealed record Microphone(int DeviceNumber, string Name);

/// <summary>Records the microphone as 16 kHz mono samples, the format Whisper expects.</summary>
public sealed class AudioRecorder : IDisposable
{
    public const int SampleRate = 16000;

    private readonly object _gate = new();
    private WaveIn? _waveIn;
    private List<float> _samples = new();
    private TaskCompletionSource? _stopped;

    public bool IsRecording => _waveIn is not null;

    /// <summary>Peak level of the latest buffer, 0..1. Raised on a background thread.</summary>
    public event Action<float>? LevelChanged;

    public static IReadOnlyList<Microphone> ListMicrophones()
    {
        var result = new List<Microphone> { new(-1, "Windows default") };
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            result.Add(new Microphone(i, WaveIn.GetCapabilities(i).ProductName));
        }
        return result;
    }

    public void Start(int deviceNumber)
    {
        if (_waveIn is not null)
        {
            throw new InvalidOperationException("Already recording.");
        }

        lock (_gate)
        {
            _samples = new List<float>(SampleRate * 30);
        }

        // WinMM converts from the device's native format to 16 kHz / 16-bit / mono for us.
        var waveIn = new WaveIn
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50,
        };
        waveIn.DataAvailable += OnDataAvailable;
        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        waveIn.RecordingStopped += (_, e) =>
        {
            if (e.Exception is not null)
            {
                Log.Error("Recording stopped with an error", e.Exception);
            }
            _stopped?.TrySetResult();
        };

        waveIn.StartRecording();
        _waveIn = waveIn;
    }

    /// <summary>Stops recording and returns everything captured since <see cref="Start"/>.</summary>
    public async Task<float[]> StopAsync()
    {
        var waveIn = _waveIn ?? throw new InvalidOperationException("Not recording.");
        _waveIn = null;

        waveIn.StopRecording();
        // The last buffer is delivered before RecordingStopped fires.
        await Task.WhenAny(_stopped!.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        waveIn.Dispose();

        lock (_gate)
        {
            return _samples.ToArray();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var peak = 0f;
        lock (_gate)
        {
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                _samples.Add(sample);
                peak = Math.Max(peak, Math.Abs(sample));
            }
        }
        LevelChanged?.Invoke(peak);
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _waveIn = null;
    }
}
