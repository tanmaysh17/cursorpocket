using System.Diagnostics;
using System.Runtime.InteropServices;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket_App.Services;

/// <summary>
/// Watches the pointer for the cursor companion, the double-circle gesture, and the
/// both-buttons chord.
/// <para>
/// A low-level mouse hook fires on whichever thread installed it, so this owns a
/// dedicated message-pumping thread rather than borrowing the UI thread. That keeps
/// every mouse event in the system off the XAML dispatcher and stops Windows from
/// dropping the hook whenever WinUI is busy rendering (<c>LowLevelHooksTimeout</c>);
/// measured on the installed build, a fast sweep previously lost about a third of
/// its events that way. The callback allocates nothing and reports movement as a
/// single coalesced signal instead of one dispatch per event.
/// </para>
/// </summary>
public sealed class MouseActivityService : IDisposable
{
    private readonly NativeMethods.MouseHookProc _procedure;
    private readonly DoubleCircleGestureDetector _gesture = new();
    private readonly ChordActivationDetector _chord = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Threading.Timer _chordTimer;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private nint _hook;
    private uint _threadId;
    private long _latestPoint;
    private int _hasPoint;
    private int _movePending;
    private int _gestureEnabled;
    private int _gestureSensitivity = (int)MouseGestureSensitivity.Balanced;
    private bool _swallowingChord;
    private bool _started;
    private bool _disposed;

    /// <summary>WM_LBUTTONUP as an int, so it can sit in the same pattern match as the others.</summary>
    private const int LeftButtonUp = (int)NativeMethods.WmLButtonUp;

    // MSLLHOOKSTRUCT field offsets. Reading only the fields actually needed avoids
    // the per-event marshalling allocation of PtrToStructure on what is by far the
    // hottest path in the app.
    private const int LocationXOffset = 0;
    private const int LocationYOffset = 4;
    private const int FlagsOffset = 12;

    public MouseActivityService()
    {
        _procedure = HookCallback;
        // A perfectly still hold produces no further mouse messages, so the hook
        // alone would never notice the hold elapsing. The timer is what actually
        // fires the chord; the hook only starts and cancels it.
        _chordTimer = new System.Threading.Timer(_ => PollChord(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised once per pending update, not once per mouse event.</summary>
    public event EventHandler? Moved;
    public event EventHandler? DoubleCircle;

    /// <summary>Both mouse buttons were held together long enough to mean it.</summary>
    public event EventHandler? ChordHold;

    /// <summary>
    /// Gesture recognition is skipped entirely while this is false, so a user who
    /// turned the gesture off pays nothing for it.
    /// </summary>
    public bool GestureEnabled
    {
        get => Volatile.Read(ref _gestureEnabled) != 0;
        set => Volatile.Write(ref _gestureEnabled, value ? 1 : 0);
    }

    /// <summary>
    /// The detector reads this atomically on the hook thread, so Settings can change
    /// the tolerance live without replacing gesture state or blocking pointer input.
    /// </summary>
    public MouseGestureSensitivity GestureSensitivity
    {
        get => (MouseGestureSensitivity)Volatile.Read(ref _gestureSensitivity);
        set => Volatile.Write(ref _gestureSensitivity, (int)value);
    }

    /// <summary>
    /// Installs the hook and begins delivering events. Subscribe to <see cref="Moved"/>
    /// first: the signal is coalesced, so movement seen before there is a subscriber
    /// would otherwise arm the signal with nobody to consume it.
    /// </summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }
        _started = true;
        _thread = new Thread(RunHookLoop)
        {
            IsBackground = true,
            Name = "CursorPocket.MouseHook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Reads the newest pointer position and re-arms the <see cref="Moved"/> signal.
    /// The signal is re-armed first so movement arriving during this call still
    /// schedules another update instead of being lost.
    /// </summary>
    public bool TryConsumeLatestPosition(out int x, out int y)
    {
        Interlocked.Exchange(ref _movePending, 0);
        if (Volatile.Read(ref _hasPoint) == 0)
        {
            x = 0;
            y = 0;
            return false;
        }
        var packed = Interlocked.Read(ref _latestPoint);
        x = unchecked((int)(uint)(packed & 0xFFFFFFFF));
        y = (int)(packed >> 32);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _chordTimer.Dispose();
        var threadId = _threadId;
        if (threadId != 0)
        {
            NativeMethods.PostThreadMessage(threadId, NativeMethods.WmQuit, 0, 0);
        }
        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void RunHookLoop()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _procedure,
            NativeMethods.GetModuleHandle(null),
            0);
        _ready.Set();

        while (NativeMethods.GetMessage(out var message, 0, 0, 0))
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }

        if (_hook != 0)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code < 0)
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        var message = (int)wParam;
        if (message == NativeMethods.WmMouseMove)
        {
            var x = Marshal.ReadInt32(lParam, LocationXOffset);
            var y = Marshal.ReadInt32(lParam, LocationYOffset);
            Interlocked.Exchange(ref _latestPoint, unchecked((long)(uint)x) | ((long)y << 32));
            Volatile.Write(ref _hasPoint, 1);
            if (Interlocked.Exchange(ref _movePending, 1) == 0)
            {
                var handler = Moved;
                if (handler is null)
                {
                    // Never leave the signal armed with no consumer; it would latch
                    // and no further movement would ever be reported.
                    Volatile.Write(ref _movePending, 0);
                }
                else
                {
                    handler(this, EventArgs.Empty);
                }
            }
            if (Volatile.Read(ref _gestureEnabled) != 0 &&
                _gesture.Feed(
                    x,
                    y,
                    _clock.Elapsed.TotalSeconds,
                    (MouseGestureSensitivity)Volatile.Read(ref _gestureSensitivity)))
            {
                DoubleCircle?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            // Our own synthesized release must not be fed back into the detector.
            var injected = (Marshal.ReadInt32(lParam, FlagsOffset) & NativeMethods.LowLevelMouseInjected) != 0;
            if (!injected && TryHandleButton(message, out var swallow) && swallow)
            {
                // Returning non-zero keeps the event from ever reaching the app
                // underneath, which is what stops a context menu appearing behind
                // command mode.
                return 1;
            }
        }
        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>
    /// Tracks the chord and decides whether the event should be hidden from the
    /// application underneath.
    /// <para>
    /// The first button always passes through, so ordinary clicks and drags behave
    /// exactly as before. Only once the second button lands — already something
    /// nothing else asks you to do — is the event swallowed, and a release is
    /// synthesized for the button that did get through so the app is not left
    /// believing a button is still down.
    /// </para>
    /// </summary>
    private bool TryHandleButton(int message, out bool swallow)
    {
        swallow = false;
        var seconds = _clock.Elapsed.TotalSeconds;
        var isDown = message is NativeMethods.WmLButtonDown or NativeMethods.WmRButtonDown;
        var isUp = message is LeftButtonUp or NativeMethods.WmRButtonUp;
        if (!isDown && !isUp)
        {
            return false;
        }
        var button = message is NativeMethods.WmLButtonDown or LeftButtonUp
            ? MouseChordButton.Left
            : MouseChordButton.Right;

        if (isDown)
        {
            var wasHeld = _chord.IsChordHeld;
            _chord.Press(button, seconds);
            if (_chord.IsChordHeld && !wasHeld)
            {
                _swallowingChord = true;
                ReleaseHeldButtonsForApplication(button);
                ArmChordTimer();
                swallow = true;
            }
            return true;
        }

        _chord.Release(button, seconds);
        if (_swallowingChord)
        {
            // Keep hiding the rest of the chord until the hand is off the mouse,
            // so neither release lands on the app as a stray click.
            swallow = true;
            if (!_chord.IsChordHeld)
            {
                _swallowingChord = false;
                _chordTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        return true;
    }

    private void ArmChordTimer()
    {
        var remaining = _chord.SecondsUntilActivation(_clock.Elapsed.TotalSeconds) ?? 0;
        var due = (int)Math.Max(1, Math.Round(remaining * 1000));
        _chordTimer.Change(due, Timeout.Infinite);
    }

    private void PollChord()
    {
        if (_chord.ShouldActivate(_clock.Elapsed.TotalSeconds))
        {
            ChordHold?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Synthesizes an up for the button that reached the application before the
    /// chord was recognized. Without this the app keeps thinking that button is
    /// down and can sit in a capture or drag state after command mode opens.
    /// </summary>
    private static void ReleaseHeldButtonsForApplication(MouseChordButton secondButton)
    {
        var first = secondButton == MouseChordButton.Left ? MouseChordButton.Right : MouseChordButton.Left;
        var inputs = new[]
        {
            new NativeMethods.Input
            {
                Type = NativeMethods.InputMouse,
                Mouse = new NativeMethods.MouseInput
                {
                    Flags = first == MouseChordButton.Left
                        ? NativeMethods.MouseEventFLeftUp
                        : NativeMethods.MouseEventFRightUp,
                },
            },
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }
}
