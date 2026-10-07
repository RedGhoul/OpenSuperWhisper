using System.Runtime.InteropServices;
using System.Windows;

namespace OpenSuperWhisper.Services;

/// <summary>Puts text into whatever app has focus by going through the clipboard and pressing Ctrl+V.</summary>
public static class TextInserter
{
    public static async Task InsertAsync(string text, bool paste)
    {
        var previous = TryGetClipboardText();
        if (!TrySetClipboardText(text))
        {
            Log.Error("Could not write to the clipboard");
            return;
        }

        if (!paste)
        {
            return;
        }

        // The shortcut's modifiers may still be held; pasting now would arrive as e.g. Ctrl+Alt+V.
        // Wait for the user to let go rather than faking a key-up: a lone Alt-up opens the app's menu bar.
        await WaitForModifiersReleasedAsync(TimeSpan.FromSeconds(2));
        SendCtrlV();

        // Give the target app time to read the clipboard before restoring what the user had there.
        await Task.Delay(400);
        if (previous is not null)
        {
            TrySetClipboardText(previous);
        }
    }

    private static readonly ushort[] Modifiers =
        [NativeMethods.VK_CONTROL, NativeMethods.VK_MENU, NativeMethods.VK_SHIFT, NativeMethods.VK_LWIN, NativeMethods.VK_RWIN];

    private static async Task WaitForModifiersReleasedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Modifiers.Any(m => NativeMethods.IsKeyDown(m)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    private static void SendCtrlV()
    {
        NativeMethods.INPUT[] inputs =
        [
            NativeMethods.Key(NativeMethods.VK_CONTROL, up: false),
            NativeMethods.Key(NativeMethods.VK_V, up: false),
            NativeMethods.Key(NativeMethods.VK_V, up: true),
            NativeMethods.Key(NativeMethods.VK_CONTROL, up: true),
        ];

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length)
        {
            // Happens when the focused window runs elevated and we don't (UIPI).
            Log.Error($"SendInput sent {sent} of {inputs.Length} keys (error {Marshal.GetLastWin32Error()})");
        }
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    // Another app may hold the clipboard open for a moment; retry briefly.
    private static bool TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
        }
        return false;
    }
}
