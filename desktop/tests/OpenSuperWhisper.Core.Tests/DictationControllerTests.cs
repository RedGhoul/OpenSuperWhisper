namespace OpenSuperWhisper.Core.Tests;

public class DictationControllerTests
{
    private static readonly WhisperModel Model = ModelCatalog.All[0];

    private readonly AppSettings _settings = new() { ModelFileName = Model.FileName };
    private readonly FakeAudio _audio = new();
    private readonly FakeTranscriber _transcriber = new();
    private readonly FakeUi _ui = new();
    private DateTime _now = new(2026, 1, 1);
    private readonly DictationController _controller;

    public DictationControllerTests()
    {
        TestEnvironment.Install(Model);
        _controller = new DictationController(() => _settings, _audio, _transcriber, _ui, () => _now);
    }

    [Fact]
    public async Task HoldToRecordTranscribesOnReleaseAndPastes()
    {
        var armed = new List<bool>();
        _controller.CancelArmedChanged += armed.Add;

        _controller.OnRecordPressed();
        Assert.Equal(DictationState.Recording, _controller.State);
        Assert.True(_audio.IsRecording);

        _now += TimeSpan.FromSeconds(2);
        _controller.OnRecordReleased();
        await _controller.Completion;

        Assert.Equal(DictationState.Idle, _controller.State);
        Assert.Equal(("hello world ", true), Assert.Single(_ui.Inserted));
        Assert.Equal("hello world ", _controller.LastTranscription);
        Assert.Equal([true, false], armed);
        Assert.Equal(["recording", "transcribing:Fake GPU", "hide", "hide"], _ui.Events);
    }

    [Fact]
    public async Task QuickTapLatchesUntilNextPress()
    {
        _controller.OnRecordPressed();
        _now += TimeSpan.FromMilliseconds(100);
        _controller.OnRecordReleased();

        Assert.Equal(DictationState.Recording, _controller.State);

        _now += TimeSpan.FromSeconds(3);
        _controller.OnRecordPressed();
        _controller.OnRecordReleased(); // release of the second tap must not matter
        await _controller.Completion;

        Assert.Single(_ui.Inserted);
    }

    [Fact]
    public async Task ToggleModeStopsOnSecondPress()
    {
        _settings.HoldToRecord = false;

        _controller.OnRecordPressed();
        _now += TimeSpan.FromSeconds(2);
        _controller.OnRecordReleased();
        Assert.Equal(DictationState.Recording, _controller.State);

        _controller.OnRecordPressed();
        await _controller.Completion;

        Assert.Single(_ui.Inserted);
    }

    [Fact]
    public void CancelWhileRecordingDiscardsAudio()
    {
        _controller.OnRecordPressed();
        _controller.OnCancel();

        Assert.Equal(DictationState.Idle, _controller.State);
        Assert.False(_audio.IsRecording);
        Assert.Empty(_ui.Inserted);
        Assert.Equal(0, _transcriber.Calls);
    }

    [Fact]
    public async Task CancelWhileTranscribingInsertsNothing()
    {
        _transcriber.Gate = new TaskCompletionSource();
        _controller.OnRecordPressed();
        _now += TimeSpan.FromSeconds(2);
        _controller.OnRecordReleased();
        await _transcriber.Started.Task;

        _controller.OnCancel();
        await _controller.Completion;

        Assert.Equal(DictationState.Idle, _controller.State);
        Assert.Empty(_ui.Inserted);
    }

    [Fact]
    public async Task VeryShortRecordingIsDropped()
    {
        _audio.Seconds = 0.1;
        _controller.OnRecordPressed();
        _now += TimeSpan.FromSeconds(1);
        _controller.OnRecordReleased();
        await _controller.Completion;

        Assert.Equal(0, _transcriber.Calls);
        Assert.Empty(_ui.Inserted);
    }

    [Fact]
    public async Task CopyOnlyModeDoesNotPaste()
    {
        _settings.AutoPaste = false;
        _settings.AddTrailingSpace = false;

        _controller.OnRecordPressed();
        _now += TimeSpan.FromSeconds(2);
        _controller.OnRecordReleased();
        await _controller.Completion;

        Assert.Equal(("hello world", false), Assert.Single(_ui.Inserted));
    }

    [Fact]
    public async Task EmptyTranscriptionInsertsNothing()
    {
        _transcriber.Text = "";
        _controller.OnRecordPressed();
        _now += TimeSpan.FromSeconds(2);
        _controller.OnRecordReleased();
        await _controller.Completion;

        Assert.Empty(_ui.Inserted);
    }

    [Fact]
    public async Task FailureShowsErrorAndKeepsItVisible()
    {
        _transcriber.Error = new InvalidOperationException("GPU fell over");
        _controller.OnRecordPressed();
        _now += TimeSpan.FromSeconds(2);
        _controller.OnRecordReleased();
        await _controller.Completion;

        Assert.Equal(DictationState.Idle, _controller.State);
        Assert.Equal("error:Transcription failed: GPU fell over", _ui.Events[^1]);
    }

    [Fact]
    public void MicrophoneErrorIsReported()
    {
        _audio.StartError = new InvalidOperationException("No microphone found.");
        _controller.OnRecordPressed();

        Assert.Equal(DictationState.Idle, _controller.State);
        Assert.Equal("error:Could not open the microphone: No microphone found.", _ui.Events[^1]);
    }

    [Fact]
    public void MissingModelOpensSettings()
    {
        _settings.ModelFileName = "ggml-not-downloaded.bin";
        _controller.OnRecordPressed();

        Assert.Equal(DictationState.Idle, _controller.State);
        Assert.Contains("settings", _ui.Events);
        Assert.False(_audio.IsRecording);
    }

    private sealed class FakeAudio : IAudioSource
    {
        public bool IsRecording { get; private set; }
        public double Seconds { get; set; } = 2;
        public Exception? StartError { get; set; }

        public event Action<float>? LevelChanged;

        public void Start(string? microphone)
        {
            if (StartError is not null) throw StartError;
            IsRecording = true;
            LevelChanged?.Invoke(0.5f);
        }

        public Task<float[]> StopAsync()
        {
            IsRecording = false;
            return Task.FromResult(new float[(int)(Resampler.WhisperSampleRate * Seconds)]);
        }
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public string Text { get; set; } = "hello world";
        public Exception? Error { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public TaskCompletionSource Started { get; } = new();
        public int Calls { get; private set; }
        public string? ActiveBackend => "Fake GPU";

        public async Task<string> TranscribeAsync(float[] samples, AppSettings settings, WhisperModel model,
            CancellationToken ct)
        {
            Calls++;
            Started.TrySetResult();
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(ct);
            }
            if (Error is not null) throw Error;
            return Text;
        }
    }

    private sealed class FakeUi : IDictationUi
    {
        public List<string> Events { get; } = new();
        public List<(string Text, bool Paste)> Inserted { get; } = new();

        public void ShowRecording() => Events.Add("recording");
        public void ShowTranscribing(string backend) => Events.Add($"transcribing:{backend}");
        public void HideIndicator() => Events.Add("hide");
        public void SetLevel(float peak) { }
        public void ShowMessage(string message, bool isError = false) => Events.Add((isError ? "error:" : "info:") + message);
        public void OpenSettings() => Events.Add("settings");

        public Task InsertTextAsync(string text, bool paste)
        {
            Inserted.Add((text, paste));
            return Task.CompletedTask;
        }
    }
}
