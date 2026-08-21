using System.Diagnostics;
using System.Runtime.InteropServices;
using CursorPocket.Core.Services;

namespace CursorPocket_App.Services;

public sealed class MouseActivityService : IDisposable
{
    private readonly NativeMethods.MouseHookProc _procedure;
    private readonly DoubleCircleGestureDetector _gesture = new();
    private readonly ChordActivationDetector _chord = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Threading.Timer _chordTimer;
    private nint _hook;
    private bool _swallowingChord;

    /// <summary>WM_LBUTTONUP as an int, so it can sit in the same pattern match as the others.</summary>
    private const int LeftButtonUp = (int)NativeMethods.WmLButtonUp;

    public MouseActivityService()
    {
        _procedure = HookCallback;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _procedure, NativeMethods.GetModuleHandle(null), 0);
        // A perfectly still hold produces no further mouse messages, so the hook
        // alone would never notice the hold elapsing. The timer is what actually
        // fires the chord; the hook only starts and cancels it.
        _chordTimer = new System.Threading.Timer(_ => PollChord(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<(int X, int Y)>? Moved;
    public event EventHandler? DoubleCircle;

    /// <summary>Both mouse buttons were held together long enough to mean it.</summary>
    public event EventHandler? ChordHold;

    public void Dispose()
    {
        _chordTimer.Dispose();
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
        var data = Marshal.PtrToStructure<NativeMethods.LowLevelMouseHook>(lParam);
        // Our own synthesized release must not be fed back into the detector.
        var injected = (data.Flags & NativeMethods.LowLevelMouseInjected) != 0;

        if (!injected && TryHandleButton(message, out var swallow))
        {
            if (swallow)
            {
                // Returning non-zero keeps the event from ever reaching the app
                // underneath, which is what stops a context menu appearing behind
                // command mode.
                return 1;
            }
        }
        else if (message == NativeMethods.WmMouseMove)
        {
            Moved?.Invoke(this, (data.Location.X, data.Location.Y));
            if (_gesture.Feed(data.Location.X, data.Location.Y, _clock.Elapsed.TotalSeconds))
            {
                DoubleCircle?.Invoke(this, EventArgs.Empty);
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
