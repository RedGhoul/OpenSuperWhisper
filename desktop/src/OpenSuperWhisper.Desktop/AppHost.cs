using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Threading;
using OpenSuperWhisper.Core;
using OpenSuperWhisper.Views;
using SharpHook;

namespace OpenSuperWhisper;

/// <summary>Wires the tray icon, global shortcut, microphone and transcriber to the dictation flow.</summary>
public sealed class AppHost : IDictationUi, IDisposable
{
    private readonly Application _app;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly HotkeyListener _hotkeys = new();
    private readonly AudioRecorder _recorder = new();
    private readonly IndicatorWindow _indicator = new();
    private readonly DictationController _dictation;
    private TrayIcon? _tray;
    private NativeMenuItem? _copyLastItem;
    private SettingsWindow? _settingsWindow;
    private Action<Hotkey?>? _captureCallback;

    public AppHost(Application app, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _app = app;
        _desktop = desktop;
        _dictation = new DictationController(() => Settings, _recorder, Transcriber, this);
    }

    public AppSettings Settings { get; private set; } = AppSettings.Load();

    public WhisperTranscriber Transcriber { get; } = new();

    /// <summary>The backend preference in effect for this process (the native library can't be swapped later).</summary>
    public GpuBackend StartupBackend { get; private set; }

    public void Start()
    {
        StartupBackend = Settings.GpuBackend;
        WhisperTranscriber.ConfigureRuntime(StartupBackend);

        CreateTray();

        _dictation.LevelChanged += peak => Dispatcher.UIThread.Post(() => _indicator.SetLevel(peak));
        _dictation.CancelArmedChanged += armed => _hotkeys.Matcher.CancelArmed = armed;
        _hotkeys.Matcher.Hotkey = Settings.RecordHotkey;
        _hotkeys.Triggered += OnHotkey;
        _ = RunHotkeysAsync();

        if (ModelCatalog.Find(Settings.ModelFileName) is { IsDownloaded: true } model)
        {
            _ = PreloadAsync(model);
            ShowMessage($"Ready. Press {Settings.RecordHotkey} to dictate.");
        }
        else
        {
            OpenSettings();
            ShowMessage("Download a model to get started.");
        }
    }

    public void ApplySettings(AppSettings updated)
    {
        var previous = Settings;
        Settings = updated;
        Settings.Save();

        _hotkeys.Matcher.Hotkey = updated.RecordHotkey;
        if (updated.ModelFileName != previous.ModelFileName &&
            ModelCatalog.Find(updated.ModelFileName) is { IsDownloaded: true } model)
        {
            _ = PreloadAsync(model);
        }
    }

    /// <summary>The next shortcut pressed anywhere is passed to <paramref name="callback"/> (null if aborted with Esc).</summary>
    public void CaptureHotkey(Action<Hotkey?> callback)
    {
        _captureCallback = callback;
        _hotkeys.Matcher.BeginCapture();
    }

    public void CancelHotkeyCapture()
    {
        _hotkeys.Matcher.EndCapture();
        _captureCallback = null;
    }

    private async Task RunHotkeysAsync()
    {
        try
        {
            await _hotkeys.StartAsync();
        }
        catch (HookException ex)
        {
            Log.Error("Could not start the global keyboard hook", ex);
            var hint = OperatingSystem.IsMacOS()
                ? "Allow OpenSuperWhisper under System Settings → Privacy & Security → Accessibility, then restart it."
                : OperatingSystem.IsLinux()
                    ? "Global shortcuts need an X11 session or a Wayland compositor that allows input capture."
                    : ex.Message;
            Dispatcher.UIThread.Post(() => ShowMessage($"Global shortcut unavailable. {hint}", isError: true));
        }
    }

    // Raised on the hook thread.
    private void OnHotkey(HotkeyAction action, Hotkey? captured) => Dispatcher.UIThread.Post(() =>
    {
        switch (action)
        {
            case HotkeyAction.RecordPressed:
                _dictation.OnRecordPressed();
                break;
            case HotkeyAction.RecordReleased:
                _dictation.OnRecordReleased();
                break;
            case HotkeyAction.Cancel:
                _dictation.OnCancel();
                break;
            case HotkeyAction.Captured or HotkeyAction.CaptureAborted:
                var callback = _captureCallback;
                _captureCallback = null;
                callback?.Invoke(captured);
                break;
        }
        _copyLastItem!.IsEnabled = _dictation.LastTranscription is not null;
    });

    private async Task PreloadAsync(WhisperModel model)
    {
        try
        {
            await Transcriber.LoadAsync(model.LocalPath);
            _settingsWindow?.RefreshBackendStatus();
            if (_tray is not null)
            {
                _tray.ToolTipText = $"OpenSuperWhisper ({Transcriber.ActiveBackend})";
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Could not load {model.FileName}", ex);
            ShowMessage($"Could not load the model: {ex.Message}", isError: true);
        }
    }

    private void CreateTray()
    {
        var settingsItem = new NativeMenuItem("Settings…");
        settingsItem.Click += (_, _) => OpenSettings();

        _copyLastItem = new NativeMenuItem("Copy last transcription") { IsEnabled = false };
        _copyLastItem.Click += async (_, _) =>
        {
            if (_dictation.LastTranscription is { } text)
            {
                await InsertTextAsync(text, paste: false);
            }
        };

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => _desktop.Shutdown();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://OpenSuperWhisper/Assets/tray.png"))),
            ToolTipText = "OpenSuperWhisper",
            Menu = new NativeMenu { Items = { settingsItem, _copyLastItem, new NativeMenuItemSeparator(), quitItem } },
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => OpenSettings();
        TrayIcon.SetIcons(_app, new TrayIcons { _tray });
    }

    // IDictationUi

    public void ShowRecording() => _indicator.ShowRecording();

    public void ShowTranscribing(string backend) => _indicator.ShowTranscribing(backend);

    public void HideIndicator() => _indicator.Hide();

    public void SetLevel(float peak) => _indicator.SetLevel(peak);

    public void ShowMessage(string message, bool isError = false) => _indicator.ShowMessage(message, isError);

    public void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    public async Task InsertTextAsync(string text, bool paste)
    {
        var clipboard = _indicator.Clipboard;
        if (clipboard is null)
        {
            Log.Error("No clipboard available");
            return;
        }

        string? previous = null;
        try
        {
            previous = await clipboard.TryGetTextAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the clipboard", ex);
        }

        await clipboard.SetTextAsync(text);
        if (!paste)
        {
            return;
        }

        // The shortcut's modifiers may still be held; pasting now would arrive as e.g. Ctrl+Alt+V.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (_hotkeys.Matcher.AnyModifierHeld && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        _hotkeys.SimulatePaste();

        // Give the target app time to read the clipboard before restoring what the user had there.
        await Task.Delay(400);
        if (previous is not null)
        {
            await clipboard.SetTextAsync(previous);
        }
    }

    public void Dispose()
    {
        _hotkeys.Dispose();
        _recorder.Dispose();
        Transcriber.Dispose();
        _tray?.Dispose();
    }
}
