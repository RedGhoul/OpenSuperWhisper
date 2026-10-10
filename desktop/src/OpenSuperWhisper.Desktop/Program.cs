using Avalonia;
using OpenSuperWhisper.Core;

namespace OpenSuperWhisper;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var singleInstance = new Mutex(true, "OpenSuperWhisper.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Console.Error.WriteLine("OpenSuperWhisper is already running.");
            return 1;
        }

        Log.Info($"Starting OpenSuperWhisper {typeof(Program).Assembly.GetName().Version} on {Environment.OSVersion}");
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Error("Fatal error", ex);
            throw;
        }
    }

    // Also used by the XAML previewer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
