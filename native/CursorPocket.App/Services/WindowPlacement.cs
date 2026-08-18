using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace CursorPocket_App.Services;

internal static class WindowPlacement
{
    public static NativeMethods.Rect MonitorUnderPointer(bool workArea = false)
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return workArea ? info.Work : info.Monitor;
        }
        return new NativeMethods.Rect { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }

    public static int DisplayIndexUnderPointer()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var target = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var current = 0;
        var result = 0;
        NativeMethods.EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref NativeMethods.Rect rect, nint data) =>
        {
            if (monitor == target)
            {
                result = current;
                return false;
            }
            current++;
            return true;
        }, 0);
        return result;
    }

    public static void ConfigureUtilityWindow(Window window, bool topmost = true, bool excludeFromCapture = true)
    {
        var appWindow = window.AppWindow;
        appWindow.IsShownInSwitchers = false;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = topmost;
        }
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var cornerPreference = NativeMethods.DwmWindowCornerPreferenceRound;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaWindowCornerPreference,
            ref cornerPreference,
            sizeof(int));
        if (excludeFromCapture)
        {
            NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludeFromCapture);
        }
    }

    public static void ConfigureColorKeyTransparency(Window window, uint colorKey, bool noActivate = false)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var existing = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        var utilityStyles = NativeMethods.WsExLayered | NativeMethods.WsExToolWindow;
        if (noActivate)
        {
            utilityStyles |= NativeMethods.WsExNoActivate;
        }
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new nint(existing | utilityStyles));
        if (!NativeMethods.SetLayeredWindowAttributes(hwnd, colorKey, 255, NativeMethods.LwaColorKey))
        {
            throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        }
    }

    public static void FillCurrentMonitor(Window window)
    {
        var bounds = MonitorUnderPointer();
        window.AppWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
    }

    public static void ResizeInDips(Window window, int width, int height)
    {
        var scale = ScaleFor(window);
        window.AppWindow.Resize(new SizeInt32(ToPixels(width, scale), ToPixels(height, scale)));
    }

    public static void PlaceBottomRight(Window window, int width, int height, int margin = 24)
    {
        var bounds = MonitorUnderPointer(true);
        var scale = ScaleFor(window);
        var pixelWidth = ToPixels(width, scale);
        var pixelHeight = ToPixels(height, scale);
        var pixelMargin = ToPixels(margin, scale);
        window.AppWindow.MoveAndResize(new RectInt32(bounds.Right - pixelWidth - pixelMargin, bounds.Bottom - pixelHeight - pixelMargin, pixelWidth, pixelHeight));
    }

    public static void PlaceTopCenter(Window window, int width, int height, int margin = 18)
    {
        var bounds = MonitorUnderPointer(true);
        var scale = ScaleFor(window);
        var pixelWidth = ToPixels(width, scale);
        var pixelHeight = ToPixels(height, scale);
        var pixelMargin = ToPixels(margin, scale);
        window.AppWindow.MoveAndResize(new RectInt32(bounds.Left + (bounds.Right - bounds.Left - pixelWidth) / 2, bounds.Top + pixelMargin, pixelWidth, pixelHeight));
    }

    private static double ScaleFor(Window window)
    {
        var dpi = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        return Math.Max(1d, dpi / 96d);
    }

    private static int ToPixels(int dips, double scale) => Math.Max(1, (int)Math.Round(dips * scale));
}
