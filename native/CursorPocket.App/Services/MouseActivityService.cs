using System.Diagnostics;
using System.Runtime.InteropServices;
using CursorPocket.Core.Services;

namespace CursorPocket_App.Services;

public sealed class MouseActivityService : IDisposable
{
    private readonly NativeMethods.MouseHookProc _procedure;
    private readonly DoubleCircleGestureDetector _gesture = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private nint _hook;

    public MouseActivityService()
    {
        _procedure = HookCallback;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _procedure, NativeMethods.GetModuleHandle(null), 0);
    }

    public event EventHandler<(int X, int Y)>? Moved;
    public event EventHandler? DoubleCircle;

    public void Dispose()
    {
        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && (int)wParam == NativeMethods.WmMouseMove)
        {
            var data = Marshal.PtrToStructure<NativeMethods.LowLevelMouseHook>(lParam);
            Moved?.Invoke(this, (data.Location.X, data.Location.Y));
            if (_gesture.Feed(data.Location.X, data.Location.Y, _clock.Elapsed.TotalSeconds))
            {
                DoubleCircle?.Invoke(this, EventArgs.Empty);
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }
}
