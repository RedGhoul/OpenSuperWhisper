using SharpHook.Data;

namespace OpenSuperWhisper.Core;

public enum HotkeyAction
{
    None,
    RecordPressed,
    RecordReleased,
    Cancel,
    /// <summary>A new shortcut was captured; see <see cref="HotkeyResult.Captured"/>.</summary>
    Captured,
    /// <summary>Capture was abandoned with Esc.</summary>
    CaptureAborted,
}

/// <param name="Suppress">Whether the key event should be kept from reaching the focused app.</param>
public readonly record struct HotkeyResult(HotkeyAction Action, bool Suppress = false, Hotkey? Captured = null)
{
    public static HotkeyResult Ignored => default;
}

/// <summary>
/// Decides what each global key event means. Pure logic with no OS calls, so it can be tested;
/// <see cref="HotkeyListener"/> feeds it events from the system-wide hook.
/// </summary>
public sealed class HotkeyMatcher
{
    private readonly object _gate = new();
    private readonly HashSet<KeyCode> _heldModifierKeys = new();
    private Hotkey _hotkey = Hotkey.Default;
    private bool _recordKeyDown;
    private bool _capturing;
    private bool _cancelArmed;

    public Hotkey Hotkey
    {
        get { lock (_gate) return _hotkey; }
        set { lock (_gate) { _hotkey = value; _recordKeyDown = false; } }
    }

    /// <summary>While true, a bare Esc press reports <see cref="HotkeyAction.Cancel"/>.</summary>
    public bool CancelArmed
    {
        get { lock (_gate) return _cancelArmed; }
        set { lock (_gate) _cancelArmed = value; }
    }

    /// <summary>True while any Ctrl/Alt/Shift/Meta key is physically down.</summary>
    public bool AnyModifierHeld
    {
        get { lock (_gate) return _heldModifierKeys.Count > 0; }
    }

    /// <summary>The next shortcut typed is reported as <see cref="HotkeyAction.Captured"/> instead of triggering.</summary>
    public void BeginCapture()
    {
        lock (_gate) _capturing = true;
    }

    public void EndCapture()
    {
        lock (_gate) _capturing = false;
    }

    public HotkeyResult OnKeyPressed(KeyCode key, EventMask mask)
    {
        lock (_gate)
        {
            if (Hotkey.IsModifierKey(key))
            {
                _heldModifierKeys.Add(key);
                return HotkeyResult.Ignored;
            }

            var modifiers = Hotkey.FromMask(mask);

            if (_capturing)
            {
                if (key == KeyCode.VcEscape && modifiers == HotkeyModifiers.None)
                {
                    _capturing = false;
                    return new HotkeyResult(HotkeyAction.CaptureAborted, Suppress: true);
                }
                // A bare letter would fire every time the user types it.
                if (modifiers == HotkeyModifiers.None && !Hotkey.IsFunctionKey(key))
                {
                    return new HotkeyResult(HotkeyAction.None, Suppress: true);
                }
                _capturing = false;
                return new HotkeyResult(HotkeyAction.Captured, Suppress: true, new Hotkey(modifiers, key));
            }

            if (key == _hotkey.Key && modifiers == _hotkey.Modifiers)
            {
                if (_recordKeyDown)
                {
                    // Key auto-repeat while held.
                    return new HotkeyResult(HotkeyAction.None, Suppress: true);
                }
                _recordKeyDown = true;
                return new HotkeyResult(HotkeyAction.RecordPressed, Suppress: true);
            }

            if (_cancelArmed && key == KeyCode.VcEscape && modifiers == HotkeyModifiers.None)
            {
                return new HotkeyResult(HotkeyAction.Cancel, Suppress: true);
            }

            return HotkeyResult.Ignored;
        }
    }

    public HotkeyResult OnKeyReleased(KeyCode key)
    {
        lock (_gate)
        {
            if (Hotkey.IsModifierKey(key))
            {
                _heldModifierKeys.Remove(key);
                return HotkeyResult.Ignored;
            }

            // Only the main key matters: letting go of the modifiers first still ends the recording when the key goes up.
            if (key == _hotkey.Key && _recordKeyDown)
            {
                _recordKeyDown = false;
                return new HotkeyResult(HotkeyAction.RecordReleased, Suppress: true);
            }
            return HotkeyResult.Ignored;
        }
    }
}
