using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenSuperWhisper.Services;

namespace OpenSuperWhisper.Views;

/// <summary>Every change applies immediately through <see cref="AppController.ApplySettings"/>.</summary>
public partial class SettingsWindow : Window
{
    private static readonly KeyValuePair<string, string>[] Languages =
    [
        new("auto", "Auto-detect"), new("en", "English"), new("de", "German"), new("es", "Spanish"),
        new("fr", "French"), new("it", "Italian"), new("pt", "Portuguese"), new("nl", "Dutch"),
        new("pl", "Polish"), new("uk", "Ukrainian"), new("ru", "Russian"), new("tr", "Turkish"),
        new("ar", "Arabic"), new("he", "Hebrew"), new("hi", "Hindi"), new("ja", "Japanese"),
        new("ko", "Korean"), new("zh", "Chinese"), new("sv", "Swedish"), new("cs", "Czech"),
    ];

    private readonly AppController _controller;
    private readonly List<ModelItem> _models;
    private bool _loading = true;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        var settings = controller.Settings;
        _models = ModelCatalog.All.Select(m => new ModelItem(m, this)).ToList();
        ModelList.ItemsSource = _models;

        BackendBox.SelectedItem = BackendBox.Items.Cast<ComboBoxItem>()
            .First(i => (string)i.Tag == settings.GpuBackend.ToString());
        LanguageBox.ItemsSource = Languages;
        LanguageBox.SelectedValue = settings.Language;
        MicrophoneBox.ItemsSource = AudioRecorder.ListMicrophones();
        MicrophoneBox.SelectedValue = settings.MicrophoneDevice;
        if (MicrophoneBox.SelectedIndex < 0)
        {
            MicrophoneBox.SelectedIndex = 0;
        }
        HotkeyBox.Text = settings.RecordHotkey.ToString();
        HoldToRecordBox.IsChecked = settings.HoldToRecord;
        AutoPasteBox.IsChecked = settings.AutoPaste;
        TrailingSpaceBox.IsChecked = settings.AddTrailingSpace;
        PromptBox.Text = settings.InitialPrompt;

        RefreshModelState();
        RefreshBackendStatus();
        _loading = false;
    }

    public void RefreshBackendStatus()
    {
        var active = _controller.Transcriber.ActiveBackend;
        var selected = Enum.Parse<GpuBackend>((string)((ComboBoxItem)BackendBox.SelectedItem).Tag);
        BackendStatus.Text = active is null
            ? "No model loaded yet."
            : selected != _controller.StartupBackend
                ? $"Currently running on {active}. Restart OpenSuperWhisper to switch."
                : $"Currently running on {active}.";
    }

    private void RefreshModelState()
    {
        foreach (var item in _models)
        {
            item.Refresh(_controller.Settings.ModelFileName);
        }
        var model = ModelCatalog.Find(_controller.Settings.ModelFileName);
        LanguageHint.Visibility = model?.IsEnglishOnly == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Apply(Action<AppSettings> change)
    {
        if (_loading)
        {
            return;
        }
        var settings = _controller.Settings.Clone();
        change(settings);
        _controller.ApplySettings(settings);
    }

    internal void SelectModel(ModelItem item)
    {
        Apply(s => s.ModelFileName = item.Model.FileName);
        RefreshModelState();
    }

    private async void OnModelAction(object sender, RoutedEventArgs e)
    {
        var item = (ModelItem)((FrameworkElement)sender).DataContext;
        if (item.Download is { } running)
        {
            running.Cancel();
            return;
        }

        using var cts = new CancellationTokenSource();
        item.Download = cts;
        try
        {
            await ModelCatalog.DownloadAsync(item.Model, new Progress<double>(p => item.Progress = p), cts.Token);
            var current = ModelCatalog.Find(_controller.Settings.ModelFileName);
            if (current is null || !current.IsDownloaded)
            {
                SelectModel(item);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Download of {item.Model.FileName} failed", ex);
            MessageBox.Show(this, $"Download failed: {ex.Message}", "OpenSuperWhisper", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            item.Download = null;
            item.Progress = 0;
            RefreshModelState();
        }
    }

    private void OnBackendChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        Apply(s => s.GpuBackend = Enum.Parse<GpuBackend>((string)((ComboBoxItem)BackendBox.SelectedItem).Tag));
        RefreshBackendStatus();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e) =>
        Apply(s => s.Language = (string)LanguageBox.SelectedValue);

    private void OnMicrophoneChanged(object sender, SelectionChangedEventArgs e) =>
        Apply(s => s.MicrophoneDevice = (int)MicrophoneBox.SelectedValue);

    private void OnOptionChanged(object sender, RoutedEventArgs e) => Apply(s =>
    {
        s.HoldToRecord = HoldToRecordBox.IsChecked == true;
        s.AutoPaste = AutoPasteBox.IsChecked == true;
        s.AddTrailingSpace = TrailingSpaceBox.IsChecked == true;
    });

    private void OnPromptChanged(object sender, RoutedEventArgs e) => Apply(s => s.InitialPrompt = PromptBox.Text.Trim());

    // While the box has focus the global shortcut is released, so pressing it here doesn't start a recording.
    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _controller.SuspendHotkeys();
        HotkeyBox.Text = "Press a shortcut…";
    }

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = _controller.Settings.RecordHotkey.ToString();
        _controller.ResumeHotkeys();
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed)
        {
            return;
        }
        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            Keyboard.ClearFocus();
            return;
        }

        var modifiers = Hotkey.FromWpf(Keyboard.Modifiers);
        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if (modifiers == HotkeyModifiers.None && !isFunctionKey)
        {
            HotkeyHint.Text = "Use at least one of Ctrl, Alt, Shift or Win (F-keys work alone).";
            return;
        }

        var hotkey = new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        Apply(s => s.RecordHotkey = hotkey);
        HotkeyHint.Text = "Click the box, then press the new combination. Press Esc while recording to cancel.";
        Keyboard.ClearFocus();
    }

    private void OnHotkeyReset(object sender, RoutedEventArgs e)
    {
        Apply(s => s.RecordHotkey = Hotkey.Default);
        HotkeyBox.Text = Hotkey.Default.ToString();
    }

    private void OnOpenModelsFolder(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppPaths.ModelsDirectory) { UseShellExecute = true });

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (System.IO.File.Exists(Log.FilePath))
        {
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        foreach (var item in _models)
        {
            item.Download?.Cancel();
        }
        base.OnClosing(e);
    }

    internal sealed class ModelItem(WhisperModel model, SettingsWindow owner) : INotifyPropertyChanged
    {
        private double _progress;
        private CancellationTokenSource? _download;
        private bool _isSelected;

        public WhisperModel Model { get; } = model;

        public bool IsDownloaded => Model.IsDownloaded;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }
                _isSelected = value;
                OnChanged();
                if (value)
                {
                    owner.SelectModel(this);
                }
            }
        }

        public CancellationTokenSource? Download
        {
            get => _download;
            set
            {
                _download = value;
                OnChanged(nameof(ActionLabel));
                OnChanged(nameof(ProgressVisibility));
            }
        }

        public double Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                OnChanged();
            }
        }

        public string ActionLabel => Download is not null ? "Cancel" : "Download";

        public Visibility ActionVisibility => IsDownloaded ? Visibility.Collapsed : Visibility.Visible;

        public Visibility ProgressVisibility => Download is not null ? Visibility.Visible : Visibility.Collapsed;

        public void Refresh(string selectedFileName)
        {
            _isSelected = IsDownloaded && Model.FileName == selectedFileName;
            OnChanged(nameof(IsSelected));
            OnChanged(nameof(IsDownloaded));
            OnChanged(nameof(ActionVisibility));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
