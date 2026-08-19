using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace CursorPocket_App.Services;

/// <summary>
/// A per-pixel-alpha Win32 surface for the cursor companion. Keeping this out of
/// XAML avoids the opaque black fallback that Windows composition can apply to
/// transparent WinUI top-level windows on some systems.
/// </summary>
internal sealed class NativeCompanionWindow : IDisposable
{
    private const string ClassName = "CursorPocket.NativeCompanion";
    private const int WindowSize = 28;
    private static readonly object ClassLock = new();
    private static readonly NativeMethods.WindowProc WindowProcedure = WndProc;
    private static readonly Dictionary<nint, WeakReference<NativeCompanionWindow>> Instances = [];
    private static bool _classRegistered;

    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly DispatcherTimer _pulseTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly nint _hwnd;
    private string _mode;
    private bool _recording;
    private bool _disposed;
    private double _phase;
    private int _x;
    private int _y;

    public NativeCompanionWindow(string mode)
    {
        EnsureWindowClass();
        _mode = mode;
        var extendedStyle = (uint)(NativeMethods.WsExLayered | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate | 0x00000008L);
        _hwnd = NativeMethods.CreateWindowEx(
            extendedStyle,
            ClassName,
            "CursorPocket status",
            NativeMethods.WsPopup,
            0,
            0,
            WindowSize,
            WindowSize,
            0,
            0,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        lock (Instances)
        {
            Instances[_hwnd] = new WeakReference<NativeCompanionWindow>(this);
        }

        NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WdaExcludeFromCapture);
        _idleTimer.Tick += IdleTimer_Tick;
        _pulseTimer.Tick += PulseTimer_Tick;
        _pulseTimer.Start();
        Render();
    }

    public event EventHandler? OpenRequested;

    public void SetMode(string mode)
    {
        _mode = mode;
        if (mode == "off")
        {
            Hide();
            return;
        }

        if (mode == "always")
        {
            NativeMethods.GetCursorPos(out var pointer);
            Follow(pointer.X, pointer.Y);
        }
    }

    public void SetRecording(bool recording)
    {
        _recording = recording;
        Render();
        if (recording)
        {
            ShowWithoutActivation();
        }
    }

    public void Follow(int x, int y)
    {
        if (_mode == "off" || _disposed)
        {
            return;
        }

        // Keep the 4px mark just beyond the pointer's bottom-right tail rather
        // than underneath the cursor body. The 28px layered window remains a
        // generous invisible click target around that visible mark.
        _x = x + 10;
        _y = y + 12;
        ShowWithoutActivation();
        if (_mode == "while-moving" && !_recording)
        {
            _idleTimer.Stop();
            _idleTimer.Start();
        }
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _idleTimer.Stop();
        _pulseTimer.Stop();
        lock (Instances)
        {
            Instances.Remove(_hwnd);
        }
        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
        }
    }

    private static void EnsureWindowClass()
    {
        lock (ClassLock)
        {
            if (_classRegistered)
            {
                return;
            }

            var windowClass = new NativeMethods.WindowClass
            {
                WindowProcedure = WindowProcedure,
                Instance = NativeMethods.GetModuleHandle(null),
                ClassName = ClassName,
            };
            if (NativeMethods.RegisterClass(ref windowClass) == 0 && Marshal.GetLastWin32Error() != NativeMethods.ErrorClassAlreadyExists)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            _classRegistered = true;
        }
    }

    private static nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        NativeCompanionWindow? instance = null;
        lock (Instances)
        {
            if (Instances.TryGetValue(hwnd, out var weakReference))
            {
                weakReference.TryGetTarget(out instance);
            }
        }

        if (message == NativeMethods.WmLButtonUp && instance is not null)
        {
            instance.OpenRequested?.Invoke(instance, EventArgs.Empty);
            return 0;
        }
        if (message == NativeMethods.WmNcHitTest)
        {
            return 1; // HTCLIENT across the generous invisible click target.
        }
        if (message == NativeMethods.WmDestroy)
        {
            lock (Instances)
            {
                Instances.Remove(hwnd);
            }
        }
        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void IdleTimer_Tick(object? sender, object eventArgs)
    {
        _idleTimer.Stop();
        if (_mode == "while-moving" && !_recording)
        {
            Hide();
        }
    }

    private void PulseTimer_Tick(object? sender, object eventArgs)
    {
        _phase = (_phase + 0.22) % (Math.PI * 2);
        Render();
    }

    private void ShowWithoutActivation()
    {
        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HwndTopmost,
            _x,
            _y,
            WindowSize,
            WindowSize,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void Hide() => NativeMethods.ShowWindowAsync(_hwnd, 0);

    private void Render()
    {
        if (_disposed)
        {
            return;
        }

        using var bitmap = new Bitmap(WindowSize, WindowSize, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var pulse = (float)((Math.Sin(_phase) + 1) / 2);
            var ready = _recording ? Color.FromArgb(255, 255, 90, 103) : Color.FromArgb(255, 67, 224, 141);
            using var glow = new SolidBrush(Color.FromArgb((int)(34 + pulse * 34), ready.R, ready.G, ready.B));
            var glowSize = 10f + pulse * 3f;
            graphics.FillEllipse(glow, 5.5f - glowSize / 2, 5.5f - glowSize / 2, glowSize, glowSize);
            using var dot = new SolidBrush(ready);
            graphics.FillEllipse(dot, 3.5f, 3.5f, 4f, 4f);
        }

        var screenDc = NativeMethods.GetDC(0);
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmapHandle = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = NativeMethods.SelectObject(memoryDc, bitmapHandle);
        try
        {
            var destination = new NativeMethods.Point { X = _x, Y = _y };
            var source = new NativeMethods.Point();
            var size = new NativeMethods.NativeSize { Width = WindowSize, Height = WindowSize };
            var blend = new NativeMethods.BlendFunction
            {
                BlendOp = NativeMethods.AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AcSrcAlpha,
            };
            NativeMethods.UpdateLayeredWindow(_hwnd, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, NativeMethods.UlwAlpha);
        }
        finally
        {
            NativeMethods.SelectObject(memoryDc, previous);
            NativeMethods.DeleteObject(bitmapHandle);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }
}
