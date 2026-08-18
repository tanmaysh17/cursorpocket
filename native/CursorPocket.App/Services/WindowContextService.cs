using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using CursorPocket.Core.Services;

namespace CursorPocket_App.Services;

public sealed class WindowContextService : IContextCaptureService
{
    private const byte VirtualKeyControl = 0x11;
    private const byte VirtualKeyEscape = 0x1B;
    private const uint KeyUp = 0x0002;
    private const uint ClipboardUnicodeText = 13;
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "brave", "chrome", "firefox", "msedge", "opera", "opera_gx", "vivaldi",
    };

    public long SnapshotForegroundWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0)
        {
            return 0;
        }
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == Environment.ProcessId ? 0 : hwnd;
    }

    public async Task<string?> ReadSelectedTextAsync(long sourceWindow, CancellationToken cancellationToken = default)
    {
        var automationSelection = await Task.Run(() => ReadAutomationSelection((nint)sourceWindow), cancellationToken);
        if (!string.IsNullOrWhiteSpace(automationSelection))
        {
            return automationSelection.Trim();
        }
        if (!Activate((nint)sourceWindow))
        {
            return null;
        }
        await Task.Delay(60, cancellationToken);
        var before = NativeMethods.GetClipboardSequenceNumber();
        SendControlKey((byte)'C');
        return await WaitForClipboardAsync(before, cancellationToken);
    }

    public async Task<string?> ReadBrowserLinkAsync(long sourceWindow, CancellationToken cancellationToken = default)
    {
        var hwnd = (nint)sourceWindow;
        if (!IsSupportedBrowser(hwnd) || !Activate(hwnd))
        {
            return null;
        }
        await Task.Delay(60, cancellationToken);
        var before = NativeMethods.GetClipboardSequenceNumber();
        SendControlKey((byte)'L');
        await Task.Delay(45, cancellationToken);
        SendControlKey((byte)'C');
        Key(VirtualKeyEscape);
        var value = await WaitForClipboardAsync(before, cancellationToken);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? value : null;
    }

    public void RestoreFocus(long sourceWindow) => Activate((nint)sourceWindow);

    private static bool Activate(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }
        // Restore only genuinely minimized windows. Calling SW_RESTORE on a healthy
        // maximized window is the regression that used to unmaximize source apps.
        if (WindowActivationPolicy.ShouldIssueRestore(NativeMethods.IsIconic(hwnd)))
        {
            NativeMethods.ShowWindowAsync(hwnd, NativeMethods.SwRestore);
        }
        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (NativeMethods.GetForegroundWindow() == hwnd)
            {
                return true;
            }
            Thread.Sleep(15);
        }
        return false;
    }

    private static bool IsSupportedBrowser(nint hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
        {
            return false;
        }
        try
        {
            return Browsers.Contains(Process.GetProcessById((int)processId).ProcessName);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<string?> WaitForClipboardAsync(uint previousSequence, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NativeMethods.GetClipboardSequenceNumber() != previousSequence)
            {
                return ReadClipboardText();
            }
            await Task.Delay(25, cancellationToken);
        }
        return null;
    }

    private static string? ReadClipboardText()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!NativeMethods.OpenClipboard(0))
            {
                Thread.Sleep(15);
                continue;
            }
            try
            {
                var handle = NativeMethods.GetClipboardData(ClipboardUnicodeText);
                if (handle == 0)
                {
                    return null;
                }
                var pointer = NativeMethods.GlobalLock(handle);
                if (pointer == 0)
                {
                    return null;
                }
                try
                {
                    return Marshal.PtrToStringUni(pointer)?.Trim();
                }
                finally
                {
                    NativeMethods.GlobalUnlock(handle);
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
        }
        return null;
    }

    private static string? ReadAutomationSelection(nint hwnd)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
        {
            return null;
        }
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            var providers = root.FindAll(
                TreeScope.Element | TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true));
            foreach (AutomationElement provider in providers)
            {
                if (provider.GetCurrentPattern(TextPattern.Pattern) is not TextPattern pattern)
                {
                    continue;
                }
                foreach (var selection in pattern.GetSelection())
                {
                    var value = selection.GetText(-1);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            // Clipboard copy remains the compatibility fallback for apps that do
            // not expose a stable UI Automation text provider.
        }
        return null;
    }

    private static void SendControlKey(byte key)
    {
        NativeMethods.keybd_event(VirtualKeyControl, 0, 0, 0);
        Key(key);
        NativeMethods.keybd_event(VirtualKeyControl, 0, KeyUp, 0);
    }

    private static void Key(byte key)
    {
        NativeMethods.keybd_event(key, 0, 0, 0);
        NativeMethods.keybd_event(key, 0, KeyUp, 0);
    }
}
