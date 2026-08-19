using Windows.System;

namespace CursorPocket_App.Services;

internal sealed class PaletteHotkeyService : IDisposable
{
    private const int FirstId = 0xC20;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmSetEnabled = 0x8001;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _enabledChanged = new(false);
    private readonly Thread _thread;
    private readonly NativeMethods.WindowProc _windowProcedure;
    private readonly Dictionary<int, (VirtualKey Key, bool Shift)> _commands = new();
    private nint _window;
    private bool _disposed;
    private bool _enabled;
    private static readonly (VirtualKey Key, bool Shift)[] Definitions =
    [
        (VirtualKey.S, false),
        (VirtualKey.V, false),
        (VirtualKey.V, true),
        (VirtualKey.A, false),
        (VirtualKey.T, false),
        (VirtualKey.L, false),
        (VirtualKey.O, false),
        (VirtualKey.R, false),
        (VirtualKey.W, false),
        (VirtualKey.D, false),
        (VirtualKey.P, false),
        (VirtualKey.Escape, false),
    ];

    public PaletteHotkeyService()
    {
        _windowProcedure = HandleMessage;
        _thread = new Thread(RunMessageWindow)
        {
            IsBackground = true,
            Name = "CursorPocket.CommandKeys",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(2));
    }

    public event EventHandler<PaletteHotkeyEventArgs>? Invoked;

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _enabled == enabled)
        {
            return;
        }
        _enabled = enabled;
        _enabledChanged.Reset();
        if (!NativeMethods.PostMessage(_window, WmSetEnabled, enabled ? 1u : 0u, 0) ||
            !_enabledChanged.Wait(TimeSpan.FromMilliseconds(500)))
        {
            throw new InvalidOperationException("CursorPocket could not update its command keys.");
        }
    }

    private void RunMessageWindow()
    {
        var className = $"CursorPocket.CommandKeys.{Environment.ProcessId}.{Guid.NewGuid():N}";
        var windowClass = new NativeMethods.WindowClass
        {
            ClassName = className,
            WindowProcedure = _windowProcedure,
            Instance = NativeMethods.GetModuleHandle(null),
        };
        NativeMethods.RegisterClass(ref windowClass);
        _window = NativeMethods.CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, -3, 0, windowClass.Instance, 0);

        _ready.Set();

        while (NativeMethods.GetMessage(out var message, 0, 0, 0))
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }
    }

    private nint HandleMessage(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (message == NativeMethods.WmHotkey && _commands.TryGetValue((int)wParam, out var command))
        {
            Invoked?.Invoke(this, new PaletteHotkeyEventArgs(command.Key, command.Shift));
            return 0;
        }
        if (message == WmSetEnabled)
        {
            if (wParam != 0) RegisterCommands(hwnd); else UnregisterCommands(hwnd);
            _enabledChanged.Set();
            return 0;
        }
        if (message == NativeMethods.WmClose)
        {
            UnregisterCommands(hwnd);
            NativeMethods.DestroyWindow(hwnd);
            return 0;
        }
        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void RegisterCommands(nint hwnd)
    {
        UnregisterCommands(hwnd);
        for (var index = 0; index < Definitions.Length; index++)
        {
            var id = FirstId + index;
            var definition = Definitions[index];
            var modifiers = ModNoRepeat | (definition.Shift ? ModShift : 0);
            if (NativeMethods.RegisterHotKey(hwnd, id, modifiers, (uint)definition.Key))
            {
                _commands[id] = definition;
            }
        }
    }

    private void UnregisterCommands(nint hwnd)
    {
        foreach (var id in _commands.Keys)
        {
            NativeMethods.UnregisterHotKey(hwnd, id);
        }
        _commands.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_window != 0)
        {
            NativeMethods.PostMessage(_window, NativeMethods.WmClose, 0, 0);
        }
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
        _enabledChanged.Dispose();
    }
}

internal sealed record PaletteHotkeyEventArgs(VirtualKey Key, bool Shift);
