using System.Windows;
using System.Windows.Threading;
using OpenSuperWhisper.Services;

namespace OpenSuperWhisper;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\OpenSuperWhisper.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("OpenSuperWhisper is already running. Look for its icon in the system tray.",
                "OpenSuperWhisper", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        Log.Info($"Starting OpenSuperWhisper {typeof(App).Assembly.GetName().Version}");

        _controller = new AppController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception", e.Exception);
        MessageBox.Show(e.Exception.Message, "OpenSuperWhisper error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
