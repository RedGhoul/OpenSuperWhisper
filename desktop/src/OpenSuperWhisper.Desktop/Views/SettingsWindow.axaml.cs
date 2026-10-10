using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenSuperWhisper.Core;

namespace OpenSuperWhisper.Views;

/// <summary>Every change applies immediately through <see cref="AppHost.ApplySettings"/>.</summary>
public partial class SettingsWindow : Window
{
    private static readonly (string Code, string Name)[] Languages =
    [
        ("auto", "Auto-detect"), ("en", "English"), ("de", "German"), ("es", "Spanish"), ("fr", "French"),
        ("it", "Italian"), ("pt", "Portuguese"), ("nl", "Dutch"), ("pl", "Polish"), ("uk", "Ukrainian"),
        ("ru", "Russian"), ("tr", "Turkish"), ("ar", "Arabic"), ("he", "Hebrew"), ("hi", "Hindi"),
        ("ja", "Japanese"), ("ko", "Korean"), ("zh", "Chinese"), ("sv", "Swedish"), ("cs", "Czech"),
    ];

    private const string DefaultMicrophone = "System default";

    private readonly AppHost _host;
    private readonly List<ModelRow> _rows = new();
    private readonly List<string?> _microphones = new();
    private bool _loading = true;

    // Parameterless constructor for the XAML previewer.
    public SettingsWindow() : this(null!)
    {
    }

    public SettingsWindow(AppHost host)
    {
        _host = host;
        InitializeComponent();
        if (host is null)
        {
            return;
        }

        var settings = host.Settings;
        foreach (var model in ModelCatalog.All)
        {
            var row = new ModelRow(model, this);
            _rows.Add(row);
            ModelList.Children.Add(row.View);
        }
        ModelsFolderHint.Text = $"Models are stored in {AppPaths.ModelsDirectory}";

        BackendBox.SelectedItem = BackendBox.Items.Cast<ComboBoxItem>()
            .First(i => (string)i.Tag! == settings.GpuBackend.ToString());

        LanguageBox.ItemsSource = Languages.Select(l => l.Name).ToList();
        LanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Code == settings.Language));

        _microphones.Add(null);
        try
        {
            _microphones.AddRange(AudioRecorder.ListMicrophones());
        }
        catch (Exception ex)
        {
            Log.Error("Could not list microphones", ex);
        }
        MicrophoneBox.ItemsSource = _microphones.Select(m => m ?? DefaultMicrophone).ToList();
        MicrophoneBox.SelectedIndex = Math.Max(0, _microphones.IndexOf(settings.Microphone));

        HotkeyText.Text = settings.RecordHotkey.ToString();
        HoldToRecordBox.IsChecked = settings.HoldToRecord;
        AutoPasteBox.IsChecked = settings.AutoPaste;
        TrailingSpaceBox.IsChecked = settings.AddTrailingSpace;
        PromptBox.Text = settings.InitialPrompt;
        LogHint.Text = $"Log file: {Log.FilePath}";

        RefreshModels();
        RefreshBackendStatus();
        _loading = false;
    }

    public void RefreshBackendStatus()
    {
        var active = _host.Transcriber.ActiveBackend;
        var selected = SelectedBackend();
        BackendStatus.Text = active is null
            ? "No model loaded yet."
            : selected != _host.StartupBackend
                ? $"Running on {active}. Restart OpenSuperWhisper to switch."
                : $"Running on {active}.";
    }

    private GpuBackend SelectedBackend() =>
        Enum.Parse<GpuBackend>((string)((ComboBoxItem)BackendBox.SelectedItem!).Tag!);

    private void RefreshModels()
    {
        foreach (var row in _rows)
        {
            row.Refresh(_host.Settings.ModelFileName);
        }
        LanguageHint.IsVisible = ModelCatalog.Find(_host.Settings.ModelFileName)?.IsEnglishOnly == true;
    }

    private void Apply(Action<AppSettings> change)
    {
        if (_loading)
        {
            return;
        }
        var settings = _host.Settings.Clone();
        change(settings);
        _host.ApplySettings(settings);
    }

    private void SelectModel(WhisperModel model)
    {
        Apply(s => s.ModelFileName = model.FileName);
        RefreshModels();
    }

    private async Task DownloadAsync(ModelRow row)
    {
        if (row.Download is { } running)
        {
            await running.CancelAsync();
            return;
        }

        using var cts = new CancellationTokenSource();
        row.Download = cts;
        row.Refresh(_host.Settings.ModelFileName);
        try
        {
            var progress = new Progress<double>(p => row.Progress.Value = p * 100);
            await ModelCatalog.DownloadAsync(row.Model, progress, cts.Token);
            if (ModelCatalog.Find(_host.Settings.ModelFileName) is not { IsDownloaded: true })
            {
                SelectModel(row.Model);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error($"Download of {row.Model.FileName} failed", ex);
            _host.ShowMessage($"Download failed: {ex.Message}", isError: true);
        }
        finally
        {
            row.Download = null;
            RefreshModels();
        }
    }

    private void OnBackendChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        Apply(s => s.GpuBackend = SelectedBackend());
        RefreshBackendStatus();
    }

    private void OnLanguageChanged(object? sender, SelectionChangedEventArgs e) =>
        Apply(s => s.Language = Languages[Math.Max(0, LanguageBox.SelectedIndex)].Code);

    private void OnMicrophoneChanged(object? sender, SelectionChangedEventArgs e) =>
        Apply(s => s.Microphone = _microphones[Math.Max(0, MicrophoneBox.SelectedIndex)]);

    private void OnOptionChanged(object? sender, RoutedEventArgs e) => Apply(s =>
    {
        s.HoldToRecord = HoldToRecordBox.IsChecked == true;
        s.AutoPaste = AutoPasteBox.IsChecked == true;
        s.AddTrailingSpace = TrailingSpaceBox.IsChecked == true;
    });

    private void OnPromptChanged(object? sender, RoutedEventArgs e) =>
        Apply(s => s.InitialPrompt = PromptBox.Text?.Trim() ?? "");

    // The shortcut is captured by the global hook itself, so what is recorded is exactly what it will match.
    private void OnChangeHotkey(object? sender, RoutedEventArgs e)
    {
        HotkeyText.Text = "Press a shortcut… (Esc to keep the current one)";
        ChangeHotkeyButton.IsEnabled = false;
        _host.CaptureHotkey(hotkey => Dispatcher.UIThread.Post(() =>
        {
            if (hotkey is not null)
            {
                Apply(s => s.RecordHotkey = hotkey);
            }
            HotkeyText.Text = _host.Settings.RecordHotkey.ToString();
            ChangeHotkeyButton.IsEnabled = true;
        }));
    }

    private void OnResetHotkey(object? sender, RoutedEventArgs e)
    {
        Apply(s => s.RecordHotkey = Hotkey.Default);
        HotkeyText.Text = Hotkey.Default.ToString();
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var row in _rows)
        {
            row.Download?.Cancel();
        }
        _host.CancelHotkeyCapture();
        base.OnClosed(e);
    }

    /// <summary>One model in the list: select, download with progress, or cancel.</summary>
    private sealed class ModelRow
    {
        private readonly RadioButton _select;
        private readonly Button _action;
        private readonly SettingsWindow _owner;

        public ModelRow(WhisperModel model, SettingsWindow owner)
        {
            Model = model;
            _owner = owner;

            _select = new RadioButton { GroupName = "model", VerticalAlignment = VerticalAlignment.Center };
            _select.IsCheckedChanged += (_, _) =>
            {
                if (_select.IsChecked == true && !_owner._loading && _owner._host.Settings.ModelFileName != Model.FileName)
                {
                    _owner.SelectModel(Model);
                }
            };

            _action = new Button { MinWidth = 96, VerticalAlignment = VerticalAlignment.Center };
            _action.Click += async (_, _) => await _owner.DownloadAsync(this);

            Progress = new ProgressBar { Height = 4, MinHeight = 4, Margin = new(0, 4, 0, 0), IsVisible = false };

            var title = new TextBlock { FontWeight = FontWeight.SemiBold, Text = $"{model.DisplayName}  ·  {model.Size}" };
            var description = new TextBlock { Text = model.Description, Opacity = 0.65, TextWrapping = TextWrapping.Wrap };
            var text = new StackPanel { Margin = new(8, 0), Children = { title, description, Progress } };

            var grid = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Margin = new(0, 6) };
            grid.Children.Add(_select);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            Grid.SetColumn(_action, 2);
            grid.Children.Add(_action);
            View = grid;
        }

        public WhisperModel Model { get; }
        public Control View { get; }
        public ProgressBar Progress { get; }
        public CancellationTokenSource? Download { get; set; }

        public void Refresh(string selectedFileName)
        {
            var downloaded = Model.IsDownloaded;
            _select.IsEnabled = downloaded;
            _select.IsChecked = downloaded && Model.FileName == selectedFileName;
            _action.IsVisible = !downloaded;
            _action.Content = Download is null ? "Download" : "Cancel";
            Progress.IsVisible = Download is not null;
            if (Download is null)
            {
                Progress.Value = 0;
            }
        }
    }
}
