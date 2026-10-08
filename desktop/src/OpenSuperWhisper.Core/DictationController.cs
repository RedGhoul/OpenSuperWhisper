namespace OpenSuperWhisper.Core;

/// <summary>What the dictation flow needs from the UI layer.</summary>
public interface IDictationUi
{
    void ShowRecording();
    void ShowTranscribing(string backend);
    void HideIndicator();
    void SetLevel(float peak);
    void ShowMessage(string message, bool isError = false);
    void OpenSettings();

    /// <summary>Copies the text and, when <paramref name="paste"/> is set, pastes it into the focused app.</summary>
    Task InsertTextAsync(string text, bool paste);
}

public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
}

/// <summary>
/// The dictation flow: shortcut → record → transcribe → insert text. All methods must be called on the UI thread.
/// </summary>
public sealed class DictationController
{
    /// <summary>
    /// In hold-to-record mode a tap shorter than this keeps recording until the next press,
    /// so both "hold and talk" and "tap, talk, tap" work.
    /// </summary>
    public static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(350);

    /// <summary>Shorter recordings are treated as accidental and dropped.</summary>
    public static readonly TimeSpan MinimumRecording = TimeSpan.FromMilliseconds(300);

    private readonly Func<AppSettings> _settings;
    private readonly IAudioSource _audio;
    private readonly ITranscriber _transcriber;
    private readonly IDictationUi _ui;
    private readonly Func<DateTime> _now;

    private DateTime _recordingStartedAt;
    private bool _latched;
    private CancellationTokenSource? _transcription;

    public DictationController(Func<AppSettings> settings, IAudioSource audio, ITranscriber transcriber, IDictationUi ui,
        Func<DateTime>? now = null)
    {
        _settings = settings;
        _audio = audio;
        _transcriber = transcriber;
        _ui = ui;
        _now = now ?? (() => DateTime.UtcNow);
        _audio.LevelChanged += peak => LevelChanged?.Invoke(peak);
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    public string? LastTranscription { get; private set; }

    /// <summary>Raised whenever Esc should (or should no longer) cancel.</summary>
    public event Action<bool>? CancelArmedChanged;

    /// <summary>Microphone peak level; raised on the audio thread.</summary>
    public event Action<float>? LevelChanged;

    /// <summary>Completes when the current transcription (if any) has finished. For tests and shutdown.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    private AppSettings Settings => _settings();

    public void OnRecordPressed()
    {
        switch (State)
        {
            case DictationState.Idle:
                StartRecording();
                break;
            case DictationState.Recording when !Settings.HoldToRecord || _latched:
                Completion = StopAndTranscribeAsync();
                break;
        }
    }

    public void OnRecordReleased()
    {
        if (State != DictationState.Recording || !Settings.HoldToRecord || _latched)
        {
            return;
        }
        if (_now() - _recordingStartedAt < TapThreshold)
        {
            _latched = true;
            return;
        }
        Completion = StopAndTranscribeAsync();
    }

    public void OnCancel()
    {
        if (State == DictationState.Recording)
        {
            _ = _audio.StopAsync();
            Finish();
        }
        else
        {
            _transcription?.Cancel();
        }
    }

    private void StartRecording()
    {
        if (ModelCatalog.Find(Settings.ModelFileName) is not { IsDownloaded: true })
        {
            _ui.OpenSettings();
            _ui.ShowMessage("Download a model first.", isError: true);
            return;
        }

        try
        {
            _audio.Start(Settings.Microphone);
        }
        catch (Exception ex)
        {
            Log.Error("Could not start recording", ex);
            _ui.ShowMessage($"Could not open the microphone: {ex.Message}", isError: true);
            return;
        }

        State = DictationState.Recording;
        _recordingStartedAt = _now();
        _latched = false;
        _ui.ShowRecording();
        CancelArmedChanged?.Invoke(true);
    }

    private async Task StopAndTranscribeAsync()
    {
        State = DictationState.Transcribing;
        var settings = Settings;

        var samples = await _audio.StopAsync();
        var model = ModelCatalog.Find(settings.ModelFileName);
        if (samples.Length < Resampler.WhisperSampleRate * MinimumRecording.TotalSeconds || model is null)
        {
            Finish();
            return;
        }

        using var cts = new CancellationTokenSource();
        _transcription = cts;
        _ui.ShowTranscribing(_transcriber.ActiveBackend ?? "GPU");
        var hideIndicator = true;

        try
        {
            var started = DateTime.UtcNow;
            var text = await _transcriber.TranscribeAsync(samples, settings, model, cts.Token);
            Log.Info($"Transcribed {samples.Length / (double)Resampler.WhisperSampleRate:F1}s of audio into " +
                     $"{text.Length} characters in {(DateTime.UtcNow - started).TotalSeconds:F2}s on {_transcriber.ActiveBackend}");

            _ui.HideIndicator();
            if (text.Length > 0)
            {
                if (settings.AddTrailingSpace)
                {
                    text += " ";
                }
                LastTranscription = text;
                await _ui.InsertTextAsync(text, settings.AutoPaste);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Transcription failed", ex);
            _ui.ShowMessage($"Transcription failed: {ex.Message}", isError: true);
            hideIndicator = false;
        }
        finally
        {
            _transcription = null;
            Finish(hideIndicator);
        }
    }

    /// <param name="hideIndicator">False when the indicator is showing a message that should stay up.</param>
    private void Finish(bool hideIndicator = true)
    {
        CancelArmedChanged?.Invoke(false);
        if (hideIndicator)
        {
            _ui.HideIndicator();
        }
        State = DictationState.Idle;
    }
}
