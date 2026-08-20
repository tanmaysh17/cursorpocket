using Windows.System;

namespace CursorPocket_App.Services;

internal sealed class PaletteHotkeyService : IDisposable
{
    private const int FirstId = 0xC20;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmSetEnabled = 0x8001;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ManualResetEventSlim _enabledChanged = new(false);
    private readonly Thread _thread;
    private readonly NativeMethods.WindowProc _windowProcedure;
    private readonly Dictionary<int, ScopedKey> _commands = new();
    private readonly IReadOnlyList<ScopedKey> _definitions;
    private nint _window;
    private bool _disposed;
    private bool _enabled;

    /// <summary>
    /// Command mode's mnemonics. Bare keys, which is only safe because they are
    /// registered solely while the palette is on screen.
    /// </summary>
    public static readonly ScopedKey[] CommandModeKeys =
    [
        new(VirtualKey.S),
        new(VirtualKey.V),
        new(VirtualKey.V, Shift: true),
        new(VirtualKey.A),
        new(VirtualKey.T),
        new(VirtualKey.L),
        new(VirtualKey.O),
        new(VirtualKey.R),
        new(VirtualKey.W),
        new(VirtualKey.D),
        new(VirtualKey.P),
        new(VirtualKey.Escape),
    ];

    public PaletteHotkeyService()
        : this(CommandModeKeys, "CursorPocket.CommandKeys")
    {
    }

    /// <summary>
    /// A scoped set of global keys, live only between <see cref="SetEnabled"/> calls.
    /// Surfaces that stay up while the user keeps working — a capture receipt, say —
    /// must pass modified combinations: bare keys would swallow ordinary typing for
    /// as long as the surface is visible.
    /// </summary>
    public PaletteHotkeyService(IReadOnlyList<ScopedKey> definitions, string threadName)
    {
        _definitions = definitions;
        _windowProcedure = HandleMessage;
        _thread = new Thread(RunMessageWindow)
        {
            IsBackground = true,
            Name = threadName,
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
        for (var index = 0; index < _definitions.Count; index++)
        {
            var id = FirstId + index;
            var definition = _definitions[index];
            var modifiers = ModNoRepeat
                | (definition.Shift ? ModShift : 0)
                | (definition.Control ? ModControl : 0)
                | (definition.Alt ? ModAlt : 0);
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

internal sealed record ScopedKey(VirtualKey Key, bool Shift = false, bool Control = false, bool Alt = false);
