using System.Windows.Input;

namespace OpenSuperWhisper.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

/// <summary>A global shortcut: modifier flags (as RegisterHotKey expects them) plus a Win32 virtual-key code.</summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private const uint VkOem3 = 0xC0; // the ` ~ key on US layouts

    /// <summary>Alt + `, the Windows counterpart of the macOS default Option + `.</summary>
    public static Hotkey Default { get; } = new(HotkeyModifiers.Alt, VkOem3);

    public Key Key => KeyInterop.KeyFromVirtualKey((int)VirtualKey);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        Key.Oem3 => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Oem2 => "/",
        Key.Oem1 => ";",
        Key.Oem7 => "'",
        Key.Oem4 => "[",
        Key.Oem6 => "]",
        Key.Oem5 => "\\",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        _ => key.ToString(),
    };

    public static HotkeyModifiers FromWpf(ModifierKeys modifiers)
    {
        var result = HotkeyModifiers.None;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= HotkeyModifiers.Windows;
        return result;
    }
}
