using System.ComponentModel;
using System.Windows.Interop;
using System.Windows.Threading;

namespace OpenSuperWhisper.Services;

/// <summary>
/// System-wide shortcuts through RegisterHotKey, received on a hidden message-only window.
/// Also reports when the record key is released so hold-to-record works.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    public const int RecordId = 1;
    public const int CancelId = 2;

    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _window;
    private readonly HashSet<int> _registered = new();
    private readonly DispatcherTimer _releasePoll;
    private uint _heldVirtualKey;

    /// <summary>Raised on the UI thread with the id of the shortcut that was pressed.</summary>
    public event Action<int>? Pressed;

    /// <summary>Raised once the key of the last <see cref="RecordId"/> press is let go.</summary>
    public event Action? RecordReleased;

    public HotkeyManager()
    {
        _window = new HwndSource(new HwndSourceParameters("OpenSuperWhisperHotkeys")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
        });
        _window.AddHook(WndProc);

        // WM_HOTKEY has no key-up counterpart, so poll the key state while it is held.
        _releasePoll = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, OnReleasePoll,
            Dispatcher.CurrentDispatcher);
        _releasePoll.Stop();
    }

    /// <summary>Registers (or re-registers) a shortcut. Throws when another app already owns it.</summary>
    public void Register(int id, HotkeyModifiers modifiers, uint virtualKey)
    {
        Unregister(id);
        if (!NativeMethods.RegisterHotKey(_window.Handle, id, (uint)modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
        {
            throw new Win32Exception();
        }
        _registered.Add(id);
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
        {
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        }
        if (id == RecordId)
        {
            _releasePoll.Stop();
        }
    }

    public void TrackRelease(uint virtualKey)
    {
        _heldVirtualKey = virtualKey;
        _releasePoll.Start();
    }

    private void OnReleasePoll(object? sender, EventArgs e)
    {
        if (NativeMethods.IsKeyDown((int)_heldVirtualKey))
        {
            return;
        }
        _releasePoll.Stop();
        RecordReleased?.Invoke();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            handled = true;
            Pressed?.Invoke(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _releasePoll.Stop();
        foreach (var id in _registered.ToList())
        {
            Unregister(id);
        }
        _window.Dispose();
    }
}
