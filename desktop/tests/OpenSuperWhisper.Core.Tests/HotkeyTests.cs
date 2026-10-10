using SharpHook.Data;

namespace OpenSuperWhisper.Core.Tests;

public class HotkeyTests
{
    private static readonly Hotkey AltBackquote = new(HotkeyModifiers.Alt, KeyCode.VcBackQuote);

    [Fact]
    public void PressAndReleaseOfShortcutAreReportedAndSuppressed()
    {
        var matcher = new HotkeyMatcher { Hotkey = AltBackquote };

        Assert.Equal(HotkeyAction.None, matcher.OnKeyPressed(KeyCode.VcLeftAlt, EventMask.LeftAlt).Action);
        var press = matcher.OnKeyPressed(KeyCode.VcBackQuote, EventMask.LeftAlt);
        var release = matcher.OnKeyReleased(KeyCode.VcBackQuote);

        Assert.Equal(new HotkeyResult(HotkeyAction.RecordPressed, Suppress: true), press);
        Assert.Equal(new HotkeyResult(HotkeyAction.RecordReleased, Suppress: true), release);
    }

    [Fact]
    public void RightHandModifierMatchesToo()
    {
        var matcher = new HotkeyMatcher { Hotkey = AltBackquote };
        Assert.Equal(HotkeyAction.RecordPressed, matcher.OnKeyPressed(KeyCode.VcBackQuote, EventMask.RightAlt).Action);
    }

    [Fact]
    public void AutoRepeatDoesNotRetrigger()
    {
        var matcher = new HotkeyMatcher { Hotkey = AltBackquote };
        matcher.OnKeyPressed(KeyCode.VcBackQuote, EventMask.LeftAlt);

        var repeat = matcher.OnKeyPressed(KeyCode.VcBackQuote, EventMask.LeftAlt);

        Assert.Equal(new HotkeyResult(HotkeyAction.None, Suppress: true), repeat);
    }

    [Theory]
    [InlineData(EventMask.None)]
    [InlineData(EventMask.LeftAlt | EventMask.LeftShift)]
    [InlineData(EventMask.LeftCtrl)]
    public void DifferentModifiersDoNotMatch(EventMask mask)
    {
        var matcher = new HotkeyMatcher { Hotkey = AltBackquote };
        Assert.Equal(HotkeyResult.Ignored, matcher.OnKeyPressed(KeyCode.VcBackQuote, mask));
    }

    [Fact]
    public void EscapeCancelsOnlyWhileArmed()
    {
        var matcher = new HotkeyMatcher();
        Assert.Equal(HotkeyResult.Ignored, matcher.OnKeyPressed(KeyCode.VcEscape, EventMask.None));

        matcher.CancelArmed = true;
        Assert.Equal(new HotkeyResult(HotkeyAction.Cancel, Suppress: true),
            matcher.OnKeyPressed(KeyCode.VcEscape, EventMask.None));
    }

    [Fact]
    public void CaptureReportsNextComboAndDoesNotTrigger()
    {
        var matcher = new HotkeyMatcher { Hotkey = AltBackquote };
        matcher.BeginCapture();

        // A bare letter is not accepted as a shortcut.
        Assert.Equal(HotkeyAction.None, matcher.OnKeyPressed(KeyCode.VcK, EventMask.None).Action);
        var result = matcher.OnKeyPressed(KeyCode.VcSpace, EventMask.LeftCtrl | EventMask.LeftShift);

        Assert.Equal(HotkeyAction.Captured, result.Action);
        Assert.Equal(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, KeyCode.VcSpace), result.Captured);
        // Capture ends after one shortcut.
        Assert.Equal(HotkeyAction.RecordPressed, matcher.OnKeyPressed(KeyCode.VcBackQuote, EventMask.LeftAlt).Action);
    }

    [Fact]
    public void FunctionKeyAloneCanBeCaptured()
    {
        var matcher = new HotkeyMatcher();
        matcher.BeginCapture();
        Assert.Equal(new Hotkey(HotkeyModifiers.None, KeyCode.VcF9),
            matcher.OnKeyPressed(KeyCode.VcF9, EventMask.None).Captured);
    }

    [Fact]
    public void EscapeAbortsCapture()
    {
        var matcher = new HotkeyMatcher();
        matcher.BeginCapture();
        Assert.Equal(HotkeyAction.CaptureAborted, matcher.OnKeyPressed(KeyCode.VcEscape, EventMask.None).Action);
    }

    [Fact]
    public void TracksHeldModifiers()
    {
        var matcher = new HotkeyMatcher();
        matcher.OnKeyPressed(KeyCode.VcLeftAlt, EventMask.LeftAlt);
        Assert.True(matcher.AnyModifierHeld);
        matcher.OnKeyReleased(KeyCode.VcLeftAlt);
        Assert.False(matcher.AnyModifierHeld);
    }

    [Fact]
    public void SettingsRoundTripKeepsHotkey()
    {
        var path = Path.Combine(AppPaths.DataDirectory, "roundtrip.json");
        var settings = new AppSettings
        {
            RecordHotkey = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Meta, KeyCode.VcF5),
            GpuBackend = GpuBackend.Vulkan,
            Microphone = "USB Mic",
        };

        settings.Save(path);
        var loaded = AppSettings.Load(path);

        Assert.Equal(settings.RecordHotkey, loaded.RecordHotkey);
        Assert.Equal(GpuBackend.Vulkan, loaded.GpuBackend);
        Assert.Equal("USB Mic", loaded.Microphone);
        Assert.Contains("\"VcF5\"", File.ReadAllText(path));
    }

    [Fact]
    public void DisplayNameIsReadable()
    {
        Assert.EndsWith(" + `", Hotkey.Default.ToString());
        Assert.Equal("F9", new Hotkey(HotkeyModifiers.None, KeyCode.VcF9).ToString());
    }
}
