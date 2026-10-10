using SharpHook;
using SharpHook.Data;
using SharpHook.Simulation;

namespace OpenSuperWhisper.Core;

/// <summary>
/// System-wide keyboard hook (libuiohook through SharpHook) on Windows, macOS and Linux, plus the key simulation
/// used to paste. Events are raised on the hook's own thread.
/// </summary>
public sealed class HotkeyListener : IDisposable
{
    private readonly SimpleGlobalHook _hook = new();
    private readonly EventSimulator _simulator = EventSimulator.Create("OpenSuperWhisper");

    public HotkeyMatcher Matcher { get; } = new();

    public event Action<HotkeyAction, Hotkey?>? Triggered;

    public HotkeyListener()
    {
        // Our own simulated Ctrl+V must not count as the user's keys.
        _hook.KeyPressed += (_, e) =>
        {
            if (!e.IsEventSimulated) Handle(e, Matcher.OnKeyPressed(e.Data.KeyCode, e.RawEvent.Mask));
        };
        _hook.KeyReleased += (_, e) =>
        {
            if (!e.IsEventSimulated) Handle(e, Matcher.OnKeyReleased(e.Data.KeyCode));
        };
    }

    /// <summary>Starts the hook on a background thread. The task completes when the hook stops or fails to start.</summary>
    public Task StartAsync() => _hook.RunAsync(GlobalHookType.Keyboard);

    /// <summary>Presses Cmd+V on macOS and Ctrl+V elsewhere in the focused app.</summary>
    public void SimulatePaste()
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
        _simulator.SimulateKeyPress(modifier);
        _simulator.SimulateKeyPress(KeyCode.VcV);
        _simulator.SimulateKeyRelease(KeyCode.VcV);
        _simulator.SimulateKeyRelease(modifier);
    }

    private void Handle(KeyboardHookEventArgs e, HotkeyResult result)
    {
        // Suppression works on Windows and macOS; on Linux the key also reaches the focused app.
        if (result.Suppress)
        {
            e.SuppressEvent = true;
        }
        if (result.Action != HotkeyAction.None)
        {
            Triggered?.Invoke(result.Action, result.Captured);
        }
    }

    public void Dispose()
    {
        _hook.Dispose();
        _simulator.Dispose();
    }
}
