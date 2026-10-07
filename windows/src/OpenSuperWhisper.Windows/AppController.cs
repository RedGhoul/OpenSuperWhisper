using System.ComponentModel;
using System.IO;
using System.Windows;
using OpenSuperWhisper.Services;
using OpenSuperWhisper.Views;
using Forms = System.Windows.Forms;

namespace OpenSuperWhisper;

/// <summary>
/// Owns the tray icon and runs the dictation flow:
/// shortcut → record → transcribe on the GPU → paste into the focused app.
/// </summary>
public sealed class AppController : IDisposable
{
    private enum State { Idle, Recording, Transcribing }

    // In hold-to-record mode a tap shorter than this keeps recording until the next press,
    // so both "hold and talk" and "tap, talk, tap" work.
    private static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan MinimumRecording = TimeSpan.FromMilliseconds(300);

    private readonly AudioRecorder _recorder = new();
    private readonly IndicatorWindow _indicator = new();
    private HotkeyManager? _hotkeys;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _copyLastItem;
    private SettingsWindow? _settingsWindow;

    private State _state = State.Idle;
    private DateTime _recordingStartedAt;
    private bool _latched;
    private CancellationTokenSource? _transcription;
    private string? _lastTranscription;

    public AppSettings Settings { get; private set; } = AppSettings.Load();

    public WhisperTranscriber Transcriber { get; } = new();

    /// <summary>The backend preference in effect for this process (the native library can't be swapped later).</summary>
    public GpuBackend StartupBackend { get; private set; }

    public void Start()
    {
        StartupBackend = Settings.GpuBackend;
        WhisperTranscriber.ConfigureRuntime(StartupBackend);

        CreateTray();

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += OnHotkeyPressed;
        _hotkeys.RecordReleased += OnRecordReleased;
        RegisterRecordHotkey();

        _recorder.LevelChanged += peak => _indicator.Dispatcher.BeginInvoke(() => _indicator.SetLevel(peak));

        if (CurrentModel() is { IsDownloaded: true } model)
        {
            _ = PreloadAsync(model);
            Notify($"Ready. Press {Settings.RecordHotkey} to dictate.");
        }
        else
        {
            ShowSettings();
            Notify("Download a model to get started.");
        }
    }

    public void ApplySettings(AppSettings updated)
    {
        var previous = Settings;
        Settings = updated;
        Settings.Save();

        if (updated.RecordHotkey != previous.RecordHotkey)
        {
            RegisterRecordHotkey();
        }
        if (updated.ModelFileName != previous.ModelFileName && CurrentModel() is { IsDownloaded: true } model)
        {
            _ = PreloadAsync(model);
        }
    }

    public void SuspendHotkeys() => _hotkeys?.Unregister(HotkeyManager.RecordId);

    public void ResumeHotkeys() => RegisterRecordHotkey();

    private WhisperModel? CurrentModel() => ModelCatalog.Find(Settings.ModelFileName);

    private void RegisterRecordHotkey()
    {
        try
        {
            _hotkeys!.Register(HotkeyManager.RecordId, Settings.RecordHotkey.Modifiers, Settings.RecordHotkey.VirtualKey);
        }
        catch (Win32Exception ex)
        {
            Log.Error($"Could not register {Settings.RecordHotkey}", ex);
            Notify($"{Settings.RecordHotkey} is already used by another app. Pick another shortcut in Settings.",
                Forms.ToolTipIcon.Warning);
        }
    }

    private async Task PreloadAsync(WhisperModel model)
    {
        try
        {
            await Transcriber.LoadAsync(model.LocalPath);
            _settingsWindow?.RefreshBackendStatus();
            UpdateTrayText();
        }
        catch (Exception ex)
        {
            Log.Error($"Could not load {model.FileName}", ex);
            Notify($"Could not load the model: {ex.Message}", Forms.ToolTipIcon.Error);
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == HotkeyManager.CancelId)
        {
            Cancel();
            return;
        }

        switch (_state)
        {
            case State.Idle:
                StartRecording();
                break;
            case State.Recording when !Settings.HoldToRecord || _latched:
                _ = StopAndTranscribeAsync();
                break;
            case State.Transcribing:
                System.Media.SystemSounds.Beep.Play();
                break;
        }
    }

    private void OnRecordReleased()
    {
        if (_state != State.Recording || !Settings.HoldToRecord || _latched)
        {
            return;
        }
        if (DateTime.UtcNow - _recordingStartedAt < TapThreshold)
        {
            _latched = true;
            return;
        }
        _ = StopAndTranscribeAsync();
    }

    private void StartRecording()
    {
        if (CurrentModel() is not { IsDownloaded: true })
        {
            ShowSettings();
            Notify("Download a model first.", Forms.ToolTipIcon.Warning);
            return;
        }

        try
        {
            _recorder.Start(Settings.MicrophoneDevice);
        }
        catch (Exception ex)
        {
            Log.Error("Could not start recording", ex);
            Notify($"Could not open the microphone: {ex.Message}", Forms.ToolTipIcon.Error);
            return;
        }

        _state = State.Recording;
        _recordingStartedAt = DateTime.UtcNow;
        _latched = false;
        _indicator.ShowRecording();

        try
        {
            _hotkeys!.Register(HotkeyManager.CancelId, HotkeyModifiers.None, NativeMethods.VK_ESCAPE);
        }
        catch (Win32Exception)
        {
            // Esc-to-cancel is a convenience; recording still works without it.
        }
        if (Settings.HoldToRecord)
        {
            _hotkeys!.TrackRelease(Settings.RecordHotkey.VirtualKey);
        }
    }

    private async Task StopAndTranscribeAsync()
    {
        _hotkeys!.Unregister(HotkeyManager.CancelId);
        _state = State.Transcribing;

        var samples = await _recorder.StopAsync();
        if (samples.Length < AudioRecorder.SampleRate * MinimumRecording.TotalSeconds || CurrentModel() is not { } model)
        {
            Finish();
            return;
        }

        _transcription = new CancellationTokenSource();
        _hotkeys.Register(HotkeyManager.CancelId, HotkeyModifiers.None, NativeMethods.VK_ESCAPE);
        _indicator.ShowTranscribing(Transcriber.ActiveBackend ?? "GPU");

        try
        {
            var started = DateTime.UtcNow;
            var text = await Transcriber.TranscribeAsync(samples, Settings, model, _transcription.Token);
            Log.Info($"Transcribed {samples.Length / (double)AudioRecorder.SampleRate:F1}s of audio in " +
                     $"{(DateTime.UtcNow - started).TotalSeconds:F2}s on {Transcriber.ActiveBackend}");

            _indicator.Hide();
            if (text.Length > 0)
            {
                if (Settings.AddTrailingSpace)
                {
                    text += " ";
                }
                _lastTranscription = text;
                _copyLastItem!.Enabled = true;
                await TextInserter.InsertAsync(text, Settings.AutoPaste);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error("Transcription failed", ex);
            Notify($"Transcription failed: {ex.Message}", Forms.ToolTipIcon.Error);
        }
        finally
        {
            _transcription.Dispose();
            _transcription = null;
            Finish();
        }
    }

    private void Cancel()
    {
        if (_state == State.Recording)
        {
            _ = _recorder.StopAsync();
            Finish();
        }
        else
        {
            _transcription?.Cancel();
        }
    }

    private void Finish()
    {
        _hotkeys?.Unregister(HotkeyManager.CancelId);
        _indicator.Hide();
        _state = State.Idle;
        UpdateTrayText();
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        _copyLastItem = new Forms.ToolStripMenuItem("Copy last transcription", null, (_, _) =>
        {
            if (_lastTranscription is not null)
            {
                _ = TextInserter.InsertAsync(_lastTranscription, paste: false);
            }
        }) { Enabled = false };
        menu.Items.Add(_copyLastItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Application.Current.Shutdown());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
        UpdateTrayText();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        return File.Exists(path) ? new System.Drawing.Icon(path) : System.Drawing.SystemIcons.Application;
    }

    private void UpdateTrayText()
    {
        if (_tray is null)
        {
            return;
        }
        var backend = Transcriber.ActiveBackend;
        var text = backend is null ? "OpenSuperWhisper" : $"OpenSuperWhisper ({backend})";
        _tray.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon limit
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    private void Notify(string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info) =>
        _tray?.ShowBalloonTip(4000, "OpenSuperWhisper", message, icon);

    public void Dispose()
    {
        _transcription?.Cancel();
        _hotkeys?.Dispose();
        _recorder.Dispose();
        Transcriber.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
    }
}
