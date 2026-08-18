using System.Collections.Concurrent;

namespace CursorPocket_App.Services;

public sealed class ScopedEscapeHotkeyService : IDisposable
{
    private const int HotkeyId = 0xC29;
    private const uint VirtualKeyEscape = 0x1B;
    private const uint ModNoRepeat = 0x4000;
    private const uint WmExecuteCommand = 0x8001;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly ConcurrentQueue<HotkeyCommand> _commands = new();
    private readonly Thread _messageThread;
    private readonly NativeMethods.WindowProc _windowProcedure;
    private readonly object _gate = new();
    private readonly List<Lease> _leases = [];
    private nint _window;
    private long _nextLease;
    private bool _registered;
    private bool _disposed;
    private Action? _activeCallback;

    public ScopedEscapeHotkeyService()
    {
        _windowProcedure = HandleMessage;
        _messageThread = new Thread(RunMessageWindow)
        {
            IsBackground = true,
            Name = "CursorPocket.EscapeKey",
        };
        _messageThread.SetApartmentState(ApartmentState.STA);
        _messageThread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    public IDisposable Capture(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(callback);
        lock (_gate)
        {
            if (!_registered)
            {
                _registered = ExecuteOnMessageThread(() =>
                    NativeMethods.RegisterHotKey(_window, HotkeyId, ModNoRepeat, VirtualKeyEscape));
                if (!_registered)
                {
                    throw new InvalidOperationException("Escape is already reserved by another application.");
                }
            }
            var lease = new Lease(this, ++_nextLease, callback);
            _leases.Add(lease);
            Volatile.Write(ref _activeCallback, callback);
            return lease;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            _leases.Clear();
            Volatile.Write(ref _activeCallback, null);
            if (_registered)
            {
                ExecuteOnMessageThread(() => NativeMethods.UnregisterHotKey(_window, HotkeyId));
                _registered = false;
            }
        }
        if (_window != 0) NativeMethods.PostMessage(_window, NativeMethods.WmClose, 0, 0);
        _messageThread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void Release(long id)
    {
        lock (_gate)
        {
            _leases.RemoveAll(lease => lease.Id == id);
            Volatile.Write(ref _activeCallback, _leases.LastOrDefault()?.Callback);
            if (_leases.Count == 0 && _registered)
            {
                ExecuteOnMessageThread(() => NativeMethods.UnregisterHotKey(_window, HotkeyId));
                _registered = false;
            }
        }
    }

    private void RunMessageWindow()
    {
        var className = $"CursorPocket.EscapeKey.{Environment.ProcessId}";
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
            Volatile.Read(ref _activeCallback)?.Invoke();
            return 0;
        }
        if (message == WmExecuteCommand)
        {
            while (_commands.TryDequeue(out var command))
            {
                try { command.Result = command.Operation(); }
                catch (Exception error) { command.Error = error; }
                finally { command.Completed.Set(); }
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
        if (Environment.CurrentManagedThreadId == _messageThread.ManagedThreadId) return operation();
        if (_window == 0) return false;
        var command = new HotkeyCommand(operation);
        _commands.Enqueue(command);
        if (!NativeMethods.PostMessage(_window, WmExecuteCommand, 0, 0) ||
            !command.Completed.Wait(TimeSpan.FromSeconds(3))) return false;
        if (command.Error is not null) throw new InvalidOperationException("The Escape hotkey operation failed.", command.Error);
        return command.Result;
    }

    private sealed class Lease(ScopedEscapeHotkeyService owner, long id, Action callback) : IDisposable
    {
        private ScopedEscapeHotkeyService? _owner = owner;
        public long Id { get; } = id;
        public Action Callback { get; } = callback;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(Id);
    }

    private sealed class HotkeyCommand(Func<bool> operation)
    {
        public Func<bool> Operation { get; } = operation;
        public ManualResetEventSlim Completed { get; } = new(false);
        public bool Result { get; set; }
        public Exception? Error { get; set; }
    }
}
