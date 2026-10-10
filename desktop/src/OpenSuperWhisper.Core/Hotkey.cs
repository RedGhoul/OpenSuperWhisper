using SharpHook.Data;

namespace OpenSuperWhisper.Core;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    /// <summary>Win on Windows, Cmd on macOS, Super on Linux.</summary>
    Meta = 8,
}

/// <summary>A global shortcut: modifiers plus one key, in SharpHook's cross-platform key codes.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, KeyCode Key)
{
    /// <summary>Alt + ` (Option + ` on macOS), the same default as the macOS app.</summary>
    public static Hotkey Default { get; } = new(HotkeyModifiers.Alt, KeyCode.VcBackQuote);

    public override string ToString()
    {
        var mac = OperatingSystem.IsMacOS();
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add(mac ? "Control" : "Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add(mac ? "Option" : "Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Meta)) parts.Add(mac ? "Cmd" : OperatingSystem.IsWindows() ? "Win" : "Super");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    public static string KeyName(KeyCode key) => key switch
    {
        KeyCode.VcBackQuote => "`",
        KeyCode.VcMinus => "-",
        KeyCode.VcEquals => "=",
        KeyCode.VcComma => ",",
        KeyCode.VcPeriod => ".",
        KeyCode.VcSlash => "/",
        KeyCode.VcBackslash => "\\",
        KeyCode.VcSemicolon => ";",
        KeyCode.VcQuote => "'",
        KeyCode.VcOpenBracket => "[",
        KeyCode.VcCloseBracket => "]",
        _ => key.ToString().StartsWith("Vc", StringComparison.Ordinal) ? key.ToString()[2..] : key.ToString(),
    };

    public static bool IsModifierKey(KeyCode key) => key is
        KeyCode.VcLeftControl or KeyCode.VcRightControl or KeyCode.VcLeftAlt or KeyCode.VcRightAlt or
        KeyCode.VcLeftShift or KeyCode.VcRightShift or KeyCode.VcLeftMeta or KeyCode.VcRightMeta;

    public static bool IsFunctionKey(KeyCode key) => key is >= KeyCode.VcF1 and <= KeyCode.VcF24;

    /// <summary>Left and right variants count the same.</summary>
    public static HotkeyModifiers FromMask(EventMask mask)
    {
        var result = HotkeyModifiers.None;
        if ((mask & EventMask.Ctrl) != 0) result |= HotkeyModifiers.Control;
        if ((mask & EventMask.Alt) != 0) result |= HotkeyModifiers.Alt;
        if ((mask & EventMask.Shift) != 0) result |= HotkeyModifiers.Shift;
        if ((mask & EventMask.Meta) != 0) result |= HotkeyModifiers.Meta;
        return result;
    }
}
