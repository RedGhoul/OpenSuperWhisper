using System.Runtime.InteropServices;
using PortAudioSharp;
using PaStream = PortAudioSharp.Stream;

namespace OpenSuperWhisper.Core;

public interface IAudioSource
{
    /// <summary>Peak level of the latest buffer, 0..1. May be raised on a background thread.</summary>
    event Action<float>? LevelChanged;

    void Start(string? microphone);

    /// <summary>Stops and returns everything recorded since <see cref="Start"/> as 16 kHz mono.</summary>
    Task<float[]> StopAsync();
}

/// <summary>Microphone capture through PortAudio (WASAPI/MME on Windows, Core Audio on macOS, ALSA/Pulse on Linux).</summary>
public sealed class AudioRecorder : IAudioSource, IDisposable
{
    private static readonly Lazy<bool> Initialized = new(() =>
    {
        PortAudio.LoadNativeLibrary();
        PortAudio.Initialize();
        return true;
    });

    private readonly object _gate = new();
    private readonly PaStream.Callback _callback;
    private PaStream? _stream;
    private List<float> _samples = new();
    private double _sampleRate;

    public event Action<float>? LevelChanged;

    public AudioRecorder()
    {
        // Kept in a field so the delegate isn't collected while native code holds it.
        _callback = OnAudio;
    }

    /// <summary>Input device names; the first entry (null) is the system default.</summary>
    public static IReadOnlyList<string> ListMicrophones()
    {
        _ = Initialized.Value;
        var names = new List<string>();
        for (var i = 0; i < PortAudio.DeviceCount; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.maxInputChannels > 0 && !names.Contains(info.name))
            {
                names.Add(info.name);
            }
        }
        return names;
    }

    public void Start(string? microphone)
    {
        if (_stream is not null)
        {
            throw new InvalidOperationException("Already recording.");
        }
        _ = Initialized.Value;

        var device = FindDevice(microphone);
        if (device == PortAudio.NoDevice)
        {
            throw new InvalidOperationException("No microphone found.");
        }
        var info = PortAudio.GetDeviceInfo(device);

        // Record at the device's own rate (many drivers refuse 16 kHz) and resample when stopping.
        _sampleRate = info.defaultSampleRate;
        lock (_gate)
        {
            _samples = new List<float>((int)_sampleRate * 30);
        }

        var parameters = new StreamParameters
        {
            device = device,
            channelCount = 1,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = info.defaultLowInputLatency,
            hostApiSpecificStreamInfo = IntPtr.Zero,
        };

        var stream = new PaStream(parameters, null, _sampleRate, PortAudio.FramesPerBufferUnspecified,
            StreamFlags.ClipOff, _callback, null);
        stream.Start();
        _stream = stream;
        Log.Info($"Recording from \"{info.name}\" at {_sampleRate} Hz");
    }

    public Task<float[]> StopAsync()
    {
        var stream = _stream ?? throw new InvalidOperationException("Not recording.");
        _stream = null;

        return Task.Run(() =>
        {
            // Stop() waits for buffers in flight, so every recorded sample has been delivered afterwards.
            stream.Stop();
            stream.Dispose();
            float[] raw;
            lock (_gate)
            {
                raw = _samples.ToArray();
            }
            return Resampler.To16k(raw, _sampleRate);
        });
    }

    private static int FindDevice(string? name)
    {
        if (name is not null)
        {
            for (var i = 0; i < PortAudio.DeviceCount; i++)
            {
                var info = PortAudio.GetDeviceInfo(i);
                if (info.maxInputChannels > 0 && info.name == name)
                {
                    return i;
                }
            }
            Log.Info($"Microphone \"{name}\" not found, using the default");
        }
        return PortAudio.DefaultInputDevice;
    }

    private StreamCallbackResult OnAudio(IntPtr input, IntPtr output, uint frameCount,
        ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
    {
        if (input == IntPtr.Zero || frameCount == 0)
        {
            return StreamCallbackResult.Continue;
        }

        var buffer = new float[frameCount];
        Marshal.Copy(input, buffer, 0, (int)frameCount);

        var peak = 0f;
        foreach (var sample in buffer)
        {
            peak = Math.Max(peak, Math.Abs(sample));
        }

        lock (_gate)
        {
            _samples.AddRange(buffer);
        }
        LevelChanged?.Invoke(peak);
        return StreamCallbackResult.Continue;
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }
}
