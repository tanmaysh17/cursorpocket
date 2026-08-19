using System.Collections.Concurrent;
using CursorPocket.Core.Services;

namespace CursorPocket_App.Services;

public sealed class GlobalHotkeyService : IHotkeyService
{
    private const int HotkeyId = 0xC07;
    private const uint WmExecuteCommand = 0x8001;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ConcurrentQueue<HotkeyCommand> _commands = new();
    private readonly Thread _messageThread;
    private readonly NativeMethods.WindowProc _windowProcedure;
    private nint _window;
    private bool _disposed;

    public GlobalHotkeyService()
    {
        _windowProcedure = HandleMessage;
        _messageThread = new Thread(RunMessageWindow)
        {
            IsBackground = true,
            Name = "CursorPocket.Hotkey",
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    public string? RegisteredShortcut { get; private set; }
    internal nint MessageWindowHandle => _window;
    public event EventHandler? Invoked;

    public bool TryRegister(string shortcut)
    {
        Unregister();
        if (_window == 0 || !TryParse(shortcut, out var modifiers, out var key))
        {
            return false;
        }
        var registered = ExecuteOnMessageThread(() => NativeMethods.RegisterHotKey(_window, HotkeyId, modifiers, key));
        if (!registered)
        {
            return false;
        }
        RegisteredShortcut = shortcut;
        return true;
    }

    public void Unregister()
    {
        if (_window != 0 && RegisteredShortcut is not null)
        {
            ExecuteOnMessageThread(() => NativeMethods.UnregisterHotKey(_window, HotkeyId));
        }
        RegisteredShortcut = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Unregister();
        if (_window != 0)
        {
            NativeMethods.PostMessage(_window, NativeMethods.WmClose, 0, 0);
        }
        _messageThread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void RunMessageWindow()
    {
        var className = $"CursorPocket.Hotkey.{Environment.ProcessId}";
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
        if (message == NativeMethods.WmHotkey && (int)wParam == HotkeyId)
        {
            Invoked?.Invoke(this, EventArgs.Empty);
            return 0;
        }
        if (message == WmExecuteCommand)
        {
            while (_commands.TryDequeue(out var command))
            {
                try
                {
                    command.Result = command.Operation();
                }
                catch (Exception error)
                {
                    command.Error = error;
                }
                finally
                {
                    command.Completed.Set();
                }
            }
            return 0;
        }
        if (message == NativeMethods.WmClose)
        {
            NativeMethods.DestroyWindow(hwnd);
            return 0;
        }
        return NativeMethods.DefWindowProc(hwnd, message, wParam, lParam);
    }

    private bool ExecuteOnMessageThread(Func<bool> operation)
    {
        if (Environment.CurrentManagedThreadId == _messageThread.ManagedThreadId)
        {
            return operation();
        }
        if (_window == 0)
        {
            return false;
        }
        var command = new HotkeyCommand(operation);
        _commands.Enqueue(command);
        if (!NativeMethods.PostMessage(_window, WmExecuteCommand, 0, 0) || !command.Completed.Wait(TimeSpan.FromSeconds(3)))
        {
            return false;
        }
        if (command.Error is not null)
        {
            throw new InvalidOperationException("The Windows hotkey operation failed.", command.Error);
        }
        return command.Result;
    }

    private static bool TryParse(string value, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        foreach (var part in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= 0x0002; break;
                case "alt": modifiers |= 0x0001; break;
                case "shift": modifiers |= 0x0004; break;
                case "win": modifiers |= 0x0008; break;
                case "space": key = 0x20; break;
                case var single when single.Length == 1: key = char.ToUpperInvariant(single[0]); break;
                default: return false;
            }
        }
        return modifiers != 0 && key != 0;
    }

    private sealed class HotkeyCommand(Func<bool> operation)
    {
        public Func<bool> Operation { get; } = operation;
        public ManualResetEventSlim Completed { get; } = new(false);
        public bool Result { get; set; }
        public Exception? Error { get; set; }
    }
}
