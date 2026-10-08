using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace OpenSuperWhisper.Views;

/// <summary>
/// Small always-on-top pill near the bottom of the screen showing recording / transcribing state and messages.
/// It is shown without activation, so the text still lands in the app the user was typing in.
/// </summary>
public partial class IndicatorWindow : Window
{
    private static readonly IBrush RecordingBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xEA, 0x43, 0x35));
    private static readonly IBrush BusyBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFB, 0xBC, 0x04));
    private static readonly IBrush InfoBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x34, 0xA8, 0x53));

    private readonly DispatcherTimer _messageTimer;

    public IndicatorWindow()
    {
        InitializeComponent();
        _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            Hide();
        };
        SizeChanged += (_, _) => PlaceAtBottomCenter();
    }

    public void ShowRecording() => ShowState(RecordingBrush, "Listening", showLevel: true);

    public void ShowTranscribing(string backend) => ShowState(BusyBrush, $"Transcribing on {backend}", showLevel: false);

    /// <summary>Shows a message that hides itself after a few seconds.</summary>
    public void ShowMessage(string message, bool isError)
    {
        ShowState(isError ? RecordingBrush : InfoBrush, message, showLevel: false);
        _messageTimer.Start();
    }

    public void SetLevel(float peak)
    {
        // Speech peaks are usually well below full scale; scale up so the bar moves visibly.
        LevelFill.Width = LevelTrack.Width * Math.Clamp(peak * 3, 0, 1);
    }

    private void ShowState(IBrush dot, string text, bool showLevel)
    {
        _messageTimer.Stop();
        Dot.Fill = dot;
        Label.Text = text;
        LevelTrack.IsVisible = showLevel;
        LevelFill.Width = 0;
        if (!IsVisible)
        {
            Show();
        }
        PlaceAtBottomCenter();
    }

    private void PlaceAtBottomCenter()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }
        var area = screen.WorkingArea;
        var width = (int)(Bounds.Width * screen.Scaling);
        var height = (int)(Bounds.Height * screen.Scaling);
        Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Bottom - height - (int)(48 * screen.Scaling));
    }
}
