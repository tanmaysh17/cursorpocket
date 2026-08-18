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

    public static void ConfigureUtilityWindow(Window window, bool topmost = true)
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
        NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WdaExcludeFromCapture);
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

    public static void PlaceBottomRight(Window window, int width, int height, int margin = 24)
    {
        var bounds = MonitorUnderPointer(true);
        window.AppWindow.MoveAndResize(new RectInt32(bounds.Right - width - margin, bounds.Bottom - height - margin, width, height));
    }

    public static void PlaceTopCenter(Window window, int width, int height, int margin = 18)
    {
        var bounds = MonitorUnderPointer(true);
        window.AppWindow.MoveAndResize(new RectInt32(bounds.Left + (bounds.Right - bounds.Left - width) / 2, bounds.Top + margin, width, height));
    }
}
