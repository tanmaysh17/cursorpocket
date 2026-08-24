using System.ComponentModel;
using System.Diagnostics;
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
/// <para>
/// The pulse is a fixed loop, so every frame is rasterized once at construction and
/// then blitted from a cached GDI bitmap. The alternative — rebuilding a Bitmap,
/// Graphics, two brushes and an HBITMAP on every tick — churned GDI handles twelve
/// times a second for the life of the process, including while the dot was hidden.
/// </para>
/// </summary>
internal sealed class NativeCompanionWindow : IDisposable
{
    private const string ClassName = "CursorPocket.NativeCompanion";
    private const int WindowSize = 28;
    // 29 frames at 80 ms reproduces the original 0.22 rad-per-tick cadence to
    // within 2%, while letting the whole cycle be pre-rendered.
    private const int PulseFrames = 29;
    private const long IdleHideMilliseconds = 900;
    // Moving a topmost layered window makes DWM recompose that region, which costs
    // roughly two milliseconds. Beyond the display refresh rate that work is
    // invisible, and a high-polling mouse can deliver hundreds of moves a second.
    private const double MinimumMoveIntervalMilliseconds = 15;
    private static readonly object ClassLock = new();
    private static readonly NativeMethods.WindowProc WindowProcedure = WndProc;
    private static readonly Dictionary<nint, WeakReference<NativeCompanionWindow>> Instances = [];
    private static bool _classRegistered;

    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _pulseTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _moveTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _moveClock = Stopwatch.StartNew();
    private readonly nint[] _readyFrames = new nint[PulseFrames];
    private readonly nint[] _recordingFrames = new nint[PulseFrames];
    private readonly nint _hwnd;
    private nint _memoryDc;
    private string _mode;
    private bool _recording;
    private bool _disposed;
    private bool _visible;
    private int _frame;
    private double _lastPositionMilliseconds = double.NegativeInfinity;
    private long _lastMoveTicks;
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
        BuildFrames();
        App.Theme.ThemeChanged += Theme_ThemeChanged;
        _idleTimer.Tick += IdleTimer_Tick;
        _pulseTimer.Tick += PulseTimer_Tick;
        _moveTimer.Tick += MoveTimer_Tick;
        Present();
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
        UpdateIdleTimer();
    }

    public void SetRecording(bool recording)
    {
        _recording = recording;
        if (recording)
        {
            ShowWithoutActivation();
        }
        Present();
        UpdateIdleTimer();
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
        var moved = _x != x + 10 || _y != y + 12;
        _x = x + 10;
        _y = y + 12;
        _lastMoveTicks = Environment.TickCount64;
        if (moved || !_visible)
        {
            ApplyPosition();
        }
        UpdateIdleTimer();
    }

    private void ApplyPosition()
    {
        var now = _moveClock.Elapsed.TotalMilliseconds;
        if (_visible && now - _lastPositionMilliseconds < MinimumMoveIntervalMilliseconds)
        {
            // Coalesce to the refresh rate and let the trailing tick place the dot
            // wherever the pointer actually came to rest.
            if (!_moveTimer.IsEnabled)
            {
                _moveTimer.Start();
            }
            return;
        }
        _lastPositionMilliseconds = now;
        _moveTimer.Stop();
        ShowWithoutActivation();
    }

    private void MoveTimer_Tick(object? sender, object eventArgs)
    {
        _moveTimer.Stop();
        if (_disposed || _mode == "off")
        {
            return;
        }
        _lastPositionMilliseconds = _moveClock.Elapsed.TotalMilliseconds;
        ShowWithoutActivation();
    }

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        App.Theme.ThemeChanged -= Theme_ThemeChanged;
        _idleTimer.Stop();
        _pulseTimer.Stop();
        _moveTimer.Stop();
        lock (Instances)
        {
            Instances.Remove(_hwnd);
        }
        if (_hwnd != 0)
        {
            NativeMethods.DestroyWindow(_hwnd);
        }
        ReleaseFrames();
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
        if (_mode == "while-moving" && !_recording &&
            Environment.TickCount64 - _lastMoveTicks >= IdleHideMilliseconds)
        {
            Hide();
        }
    }

    private void PulseTimer_Tick(object? sender, object eventArgs)
    {
        _frame = (_frame + 1) % PulseFrames;
        Present();
    }

    private void ShowWithoutActivation()
    {
        if (_visible)
        {
            MoveToCurrentPosition();
            return;
        }
        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HwndTopmost,
            _x,
            _y,
            WindowSize,
            WindowSize,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _visible = true;
        Present();
        if (!_disposed && App.AnimationsEnabled)
        {
            _pulseTimer.Start();
        }
    }

    private void MoveToCurrentPosition() =>
        // Re-asserting topmost and SHOWWINDOW on a window that is already visible and
        // already on top made Windows redo desktop z-order and DWM work on every
        // single mouse move. Once shown, a move only needs to be a move.
        NativeMethods.SetWindowPos(
            _hwnd,
            0,
            _x,
            _y,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);

    private void Hide()
    {
        NativeMethods.ShowWindowAsync(_hwnd, 0);
        _visible = false;
        _moveTimer.Stop();
        // Nothing is on screen to animate, so stop burning a timer tick and a
        // layered-window update every 80 ms.
        _pulseTimer.Stop();
        _idleTimer.Stop();
    }

    private void UpdateIdleTimer()
    {
        var wanted = _visible && _mode == "while-moving" && !_recording;
        if (wanted && !_idleTimer.IsEnabled)
        {
            _idleTimer.Start();
        }
        else if (!wanted && _idleTimer.IsEnabled)
        {
            _idleTimer.Stop();
        }
    }

    private void BuildFrames()
    {
        var screenDc = NativeMethods.GetDC(0);
        try
        {
            _memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        }
        finally
        {
            NativeMethods.ReleaseDC(0, screenDc);
        }

        var palette = App.Theme.Palette;
        var ready = palette.Selection;
        var recording = App.Theme.IsHighContrast
            ? palette.Selection
            : ColorTranslator.FromHtml(palette.IsDark ? "#FF5964" : "#D73546");
        var outline = palette.IsDark ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(190, 0, 0, 0);
        for (var index = 0; index < PulseFrames; index++)
        {
            var phase = index * (Math.PI * 2 / PulseFrames);
            _readyFrames[index] = RenderFrame(phase, ready, outline);
            _recordingFrames[index] = RenderFrame(phase, recording, outline);
        }
    }

    private static nint RenderFrame(double phase, Color ready, Color outline)
    {
        using var bitmap = new Bitmap(WindowSize, WindowSize, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var pulse = (float)((Math.Sin(phase) + 1) / 2);
            using var glow = new SolidBrush(Color.FromArgb((int)(34 + pulse * 34), ready.R, ready.G, ready.B));
            var glowSize = 10f + pulse * 3f;
            graphics.FillEllipse(glow, 5.5f - glowSize / 2, 5.5f - glowSize / 2, glowSize, glowSize);
            using var dot = new SolidBrush(ready);
            graphics.FillEllipse(dot, 3.5f, 3.5f, 4f, 4f);
            using var edge = new Pen(outline, 1f);
            graphics.DrawEllipse(edge, 3.5f, 3.5f, 4f, 4f);
        }
        return bitmap.GetHbitmap(Color.FromArgb(0));
    }

    private void Theme_ThemeChanged(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;
        ReleaseFrames();
        BuildFrames();
        Present();
    }

    private void ReleaseFrames()
    {
        for (var index = 0; index < PulseFrames; index++)
        {
            if (_readyFrames[index] != 0)
            {
                NativeMethods.DeleteObject(_readyFrames[index]);
                _readyFrames[index] = 0;
            }
            if (_recordingFrames[index] != 0)
            {
                NativeMethods.DeleteObject(_recordingFrames[index]);
                _recordingFrames[index] = 0;
            }
        }
        if (_memoryDc != 0)
        {
            NativeMethods.DeleteDC(_memoryDc);
            _memoryDc = 0;
        }
    }

    private void Present()
    {
        if (_disposed || _memoryDc == 0)
        {
            return;
        }

        var frame = (_recording ? _recordingFrames : _readyFrames)[_frame];
        if (frame == 0)
        {
            return;
        }

        var screenDc = NativeMethods.GetDC(0);
        var previous = NativeMethods.SelectObject(_memoryDc, frame);
        try
        {
            var source = new NativeMethods.Point();
            var size = new NativeMethods.NativeSize { Width = WindowSize, Height = WindowSize };
            var blend = new NativeMethods.BlendFunction
            {
                BlendOp = NativeMethods.AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AcSrcAlpha,
            };
            // Position is owned by SetWindowPos, so this repaint passes no destination
            // and avoids a redundant window move on every pulse frame.
            NativeMethods.UpdateLayeredWindowInPlace(_hwnd, screenDc, 0, ref size, _memoryDc, ref source, 0, ref blend, NativeMethods.UlwAlpha);
        }
        finally
        {
            NativeMethods.SelectObject(_memoryDc, previous);
            NativeMethods.ReleaseDC(0, screenDc);
        }
    }
}
