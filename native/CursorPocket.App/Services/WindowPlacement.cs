using CursorPocket.Core.Models;
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

    /// <summary>
    /// Identifies the monitor under the pointer for recording: its exact rectangle,
    /// plus the DXGI output index Desktop Duplication needs, which is null when the
    /// monitor cannot be addressed that way and its rectangle must be grabbed instead.
    /// <para>
    /// Capture this the moment the user asks to record, not when they later press
    /// Start — by then the pointer is over the preflight window, which Windows may
    /// have placed on a different screen.
    /// </para>
    /// </summary>
    public static (CaptureBounds Bounds, int? OutputIndex) DisplayTargetUnderPointer()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfoEx
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
            DeviceName = string.Empty,
        };
        if (!NativeMethods.GetMonitorInfoEx(monitor, ref info))
        {
            var fallback = MonitorUnderPointer();
            return (new CaptureBounds(fallback.Left, fallback.Top, fallback.Right, fallback.Bottom), null);
        }
        var bounds = new CaptureBounds(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        return (bounds, DisplayOutputLocator.FindOutputIndex(info.DeviceName));
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
        // WinUI's OverlappedPresenter can leave the one-pixel DWM non-client
        // frame visible even after hiding its title bar. Strip those styles and
        // explicitly disable the Windows 11 border color so transient surfaces
        // render as the surface itself, not a bright rectangular HWND.
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        style &= ~(NativeMethods.WsCaption |
            NativeMethods.WsThickFrame |
            NativeMethods.WsMinimizeBox |
            NativeMethods.WsMaximizeBox |
            NativeMethods.WsSysMenu);
        style |= NativeMethods.WsPopup;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, new nint(style));
        NativeMethods.SetWindowPos(
            hwnd,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged);
        var borderColor = NativeMethods.DwmColorNone;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaBorderColor,
            ref borderColor,
            sizeof(int));
        var nonClientPolicy = NativeMethods.DwmNcRenderingDisabled;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaNcRenderingPolicy,
            ref nonClientPolicy,
            sizeof(int));
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

    /// <summary>
    /// Brings one of CursorPocket's own windows to the front and gives it focus.
    /// <para>
    /// <see cref="Window.Activate"/> alone is not enough: a capture surface is
    /// created right after a transient surface hid itself, so Windows has already
    /// handed the foreground to the source app and its foreground lock refuses the
    /// change — the new window is left behind or minimized. Attaching to the current
    /// foreground thread's input queue is what makes the handover stick.
    /// </para>
    /// This restores only CursorPocket's own windows. Source windows go through
    /// <c>WindowContextService.RestoreFocus</c>, which deliberately never restores a
    /// healthy window.
    /// </summary>
    public static void ForceForeground(Window window)
    {
        window.Activate();
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            NativeMethods.ShowWindowAsync(handle, NativeMethods.IsIconic(handle) ? NativeMethods.SwRestore : NativeMethods.SwShow);
            NativeMethods.SetWindowPos(handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpShowWindow);
            NativeMethods.BringWindowToTop(handle);
            NativeMethods.SetForegroundWindow(handle);
            NativeMethods.SetFocus(handle);
            NativeMethods.SetWindowPos(handle, NativeMethods.HwndNotTopmost, 0, 0, 0, 0,
                NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    /// <summary>
    /// Moves a window to a screen position without resizing, activating, or
    /// restacking it. Used for drag tracking, where this runs once per pointer move.
    /// </summary>
    public static void MoveTo(Window window, int left, int top) =>
        NativeMethods.SetWindowPos(
            WinRT.Interop.WindowNative.GetWindowHandle(window),
            0,
            left,
            top,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);

    public static (int X, int Y) PointerPosition()
    {
        NativeMethods.GetCursorPos(out var cursor);
        return (cursor.X, cursor.Y);
    }

    public static NativeMethods.Rect BoundsOf(Window window)
    {
        NativeMethods.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(window), out var bounds);
        return bounds;
    }

    /// <summary>Work area of the display a point sits on, in physical pixels.</summary>
    public static NativeMethods.Rect WorkAreaAt(int x, int y)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = x, Y = y }, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.Work : MonitorUnderPointer(true);
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

    public static void PlaceTopRight(Window window, int width, int height, int margin = 22)
    {
        var bounds = MonitorUnderPointer(true);
        var scale = ScaleFor(window);
        var pixelWidth = ToPixels(width, scale);
        var pixelHeight = ToPixels(height, scale);
        var pixelMargin = ToPixels(margin, scale);
        window.AppWindow.MoveAndResize(new RectInt32(bounds.Right - pixelWidth - pixelMargin, bounds.Top + pixelMargin, pixelWidth, pixelHeight));
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

    public static void ClipToRoundedRegion(Window window, int width, int height, int radius)
    {
        var scale = ScaleFor(window);
        ClipToRoundedPixelRegion(window, ToPixels(width, scale), ToPixels(height, scale), ToPixels(radius, scale));
    }

    /// <summary>
    /// Clips a surface whose size was already resolved in physical pixels, so a
    /// window sized against a monitor or capture rectangle stays exactly aligned
    /// with its rounded region instead of being re-derived from rounded dips.
    /// </summary>
    public static void ClipToRoundedPixelRegion(Window window, int pixelWidth, int pixelHeight, int pixelRadius)
    {
        var pixelDiameter = Math.Max(1, pixelRadius * 2);
        var region = NativeMethods.CreateRoundRectRgn(0, 0, pixelWidth + 1, pixelHeight + 1, pixelDiameter, pixelDiameter);
        if (region == 0)
        {
            return;
        }
        if (NativeMethods.SetWindowRgn(WinRT.Interop.WindowNative.GetWindowHandle(window), region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
        }
    }

    /// <summary>
    /// Clips a surface to an arbitrary polygon (the squircle self-view). Same
    /// contract as <see cref="ClipToRoundedPixelRegion"/>: physical pixels, and
    /// only ever applied to click-through windows the user cannot drag.
    /// </summary>
    /// <summary>
    /// Removes any window region, putting the surface back on DWM's fast path.
    /// Dragging a region-clipped window visibly lags, so a draggable surface drops
    /// its clip for the duration of the drag and re-cuts it on release.
    /// </summary>
    public static void ClearWindowRegion(Window window) =>
        NativeMethods.SetWindowRgn(WinRT.Interop.WindowNative.GetWindowHandle(window), 0, true);

    public static void ClipToPolygonPixelRegion(Window window, IReadOnlyList<(int X, int Y)> points)
    {
        var nativePoints = new NativeMethods.Point[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            nativePoints[index] = new NativeMethods.Point { X = points[index].X, Y = points[index].Y };
        }
        var region = NativeMethods.CreatePolygonRgn(nativePoints, nativePoints.Length, NativeMethods.Winding);
        if (region == 0)
        {
            return;
        }
        if (NativeMethods.SetWindowRgn(WinRT.Interop.WindowNative.GetWindowHandle(window), region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
        }
    }

    public static double ScaleFor(Window window)
    {
        var dpi = NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        return Math.Max(1d, dpi / 96d);
    }

    public static int ToPixels(Window window, int dips) => ToPixels(dips, ScaleFor(window));

    private static int ToPixels(int dips, double scale) => Math.Max(1, (int)Math.Round(dips * scale));
}
