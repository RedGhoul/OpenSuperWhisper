using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using OpenSuperWhisper.Services;

namespace OpenSuperWhisper.Views;

/// <summary>
/// Small always-on-top pill near the bottom of the screen. It never takes focus, so the text
/// still gets pasted into the app the user was typing in.
/// </summary>
public partial class IndicatorWindow : Window
{
    private static readonly Brush RecordingBrush = Frozen(Color.FromRgb(0xEA, 0x43, 0x35));
    private static readonly Brush BusyBrush = Frozen(Color.FromRgb(0xFB, 0xBC, 0x04));

    public IndicatorWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeNonActivating();
        SizeChanged += (_, _) => PlaceAtBottomCenter();
    }

    public void ShowRecording()
    {
        Dot.Fill = RecordingBrush;
        LevelTrack.Visibility = Visibility.Visible;
        LevelFill.Width = 0;
        Label.Text = "Listening";
        ShowWithoutFocus();
    }

    public void ShowTranscribing(string backend)
    {
        Dot.Fill = BusyBrush;
        LevelTrack.Visibility = Visibility.Collapsed;
        Label.Text = $"Transcribing on {backend}";
        ShowWithoutFocus();
    }

    public void SetLevel(float peak)
    {
        // Speech peaks are usually well below full scale; scale up so the bar moves visibly.
        LevelFill.Width = LevelTrack.Width * Math.Clamp(peak * 3, 0, 1);
    }

    private void ShowWithoutFocus()
    {
        if (!IsVisible)
        {
            Show();
        }
        PlaceAtBottomCenter();
    }

    private void PlaceAtBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 48;
    }

    private void MakeNonActivating()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
