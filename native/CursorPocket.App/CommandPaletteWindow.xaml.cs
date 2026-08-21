using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace CursorPocket_App;

public sealed partial class CommandPaletteWindow : Window
{
    // Command mode reopens wherever the user last put it and never moves on its own.
    private const int PanelWidth = 296;
    private const int PanelHeight = 340;
    private const int PanelMargin = 22;

    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly PaletteHotkeyService _commandKeys = new();
    private (int Left, int Top) _dragOrigin;
    private (int X, int Y) _dragStart;
    private bool _dragging;
    private bool _screenshotMode;
    private bool _restoreSourceOnClose = true;
    private bool _paletteLoaded;
    private long _lastTimeoutReset;

    public CommandPaletteWindow()
    {
        InitializeComponent();
        WindowPlacement.ConfigureUtilityWindow(this, excludeFromCapture: false);
        _timeout.Tick += (_, _) => HidePalette();
        _commandKeys.Invoked += CommandKeys_Invoked;
        Activated += (_, _) =>
        {
            FocusCommandSurface();
            ResetTimeout();
        };
    }

    public void Show(long sourceWindow, string? initialMode = null)
    {
        SourceWindow = sourceWindow;
        _restoreSourceOnClose = true;
        // Wherever the user last dragged it, on the pointer's display. Acrylic keeps
        // the source readable behind it, which is what the full-screen desktop
        // snapshot used to be for.
        ApplySavedPlacement();
        PaletteHint.Text = string.IsNullOrWhiteSpace(App.Services.Settings.VideoMicrophoneName)
            ? "Press one key · Esc closes"
            : $"A uses {App.Services.Settings.VideoMicrophoneName} · Esc closes";
        if (initialMode == "screenshot") ShowScreenshotCommands(); else ShowPrimaryCommands();
        if (_paletteLoaded)
        {
            PulseStoryboard.Begin();
        }
        _commandKeys.SetEnabled(true);
        AppWindow.Show(false);
        Activate();
    }

    private void ApplySavedPlacement()
    {
        var settings = App.Services.Settings;
        var work = WindowPlacement.MonitorUnderPointer(true);
        var placement = CommandPanelPlacement.Resolve(
            ToBounds(work),
            PixelWidth(),
            PixelHeight(),
            settings.CommandPanelAnchorX,
            settings.CommandPanelAnchorY,
            PixelMargin());
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(placement.Left, placement.Top, placement.Width, placement.Height));
        // Deliberately no SetWindowRgn clip here. A window region takes the window off
        // DWM's fast path for moves, which made dragging the panel visibly lag.
        // ConfigureUtilityWindow already asks DWM for rounded corners, so the shape is
        // the same without the cost. Surfaces that are never dragged still clip.
    }

    /// <summary>
    /// Drag anywhere on the panel to move command mode; presses that land on a
    /// button are left to the button.
    /// <para>
    /// The drag is tracked here rather than handed to Windows' modal move loop
    /// (<c>WM_NCLBUTTONDOWN</c>/<c>HTCAPTION</c>): WinUI's input layer consumes the
    /// mouse messages that loop needs, so it either lagged badly or never moved the
    /// window at all. Positions come from <c>GetCursorPos</c> in physical pixels, so
    /// no DPI conversion sits between the pointer and the window.
    /// </para>
    /// </summary>
    private void Root_PointerPressed(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (eventArgs.GetCurrentPoint(Root).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed ||
            IsOverButton(eventArgs.OriginalSource as DependencyObject))
        {
            return;
        }
        var bounds = WindowPlacement.BoundsOf(this);
        _dragOrigin = (bounds.Left, bounds.Top);
        _dragStart = WindowPlacement.PointerPosition();
        _dragging = Root.CapturePointer(eventArgs.Pointer);
        if (_dragging)
        {
            eventArgs.Handled = true;
            _timeout.Stop();
        }
    }

    private void Root_PointerMovedWhileDragging(int pointerX, int pointerY) =>
        WindowPlacement.MoveTo(
            this,
            _dragOrigin.Left + (pointerX - _dragStart.X),
            _dragOrigin.Top + (pointerY - _dragStart.Y));

    private async void Root_PointerReleased(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }
        eventArgs.Handled = true;
        Root.ReleasePointerCapture(eventArgs.Pointer);
        await EndDragAsync();
    }

    private async void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (_dragging)
        {
            await EndDragAsync();
        }
    }

    private async Task EndDragAsync()
    {
        _dragging = false;
        ResetTimeout();
        var bounds = WindowPlacement.BoundsOf(this);
        var work = WindowPlacement.WorkAreaAt(
            bounds.Left + ((bounds.Right - bounds.Left) / 2),
            bounds.Top + ((bounds.Bottom - bounds.Top) / 2));
        var (anchorX, anchorY) = CommandPanelPlacement.AnchorFor(
            ToBounds(work),
            bounds.Right - bounds.Left,
            bounds.Bottom - bounds.Top,
            bounds.Left,
            bounds.Top,
            PixelMargin());
        await App.Services.UpdateCommandPanelAnchorAsync(anchorX, anchorY);
    }

    private async void Root_DoubleTapped(object sender, DoubleTappedRoutedEventArgs eventArgs)
    {
        if (IsOverButton(eventArgs.OriginalSource as DependencyObject))
        {
            return;
        }
        eventArgs.Handled = true;
        ResetTimeout();
        await App.Services.UpdateCommandPanelAnchorAsync(CommandPanelPlacement.DefaultAnchorX, CommandPanelPlacement.DefaultAnchorY);
        ApplySavedPlacement();
    }

    /// <summary>
    /// Buttons keep their clicks. Walking the visual tree rather than trusting the
    /// routed event to stop is what keeps a press on a keycap — which is a Button
    /// inside a Button's content — from starting a drag.
    /// </summary>
    private static bool IsOverButton(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ButtonBase)
            {
                return true;
            }
        }
        return false;
    }

    private static CaptureBounds ToBounds(NativeMethods.Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private int PixelWidth() => WindowPlacement.ToPixels(this, PanelWidth);
    private int PixelHeight() => WindowPlacement.ToPixels(this, PanelHeight);
    private int PixelMargin() => WindowPlacement.ToPixels(this, PanelMargin);

    public long SourceWindow { get; private set; }
    public event EventHandler<string>? CommandRequested;
    public event EventHandler? PaletteHidden;

    private void Command_Click(object sender, RoutedEventArgs eventArgs)
    {
        ResetTimeout();
        if (sender is not FrameworkElement { Tag: string command })
        {
            return;
        }
        if (command == "screenshot")
        {
            ShowScreenshotCommands();
            return;
        }
        if (command == "back")
        {
            ShowPrimaryCommands();
            return;
        }
        Request(command);
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        eventArgs.Handled = HandleCommandKey(eventArgs.Key, shiftDown);
    }

    private void CommandAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs) =>
        eventArgs.Handled = HandleCommandKey(sender.Key, sender.Modifiers.HasFlag(VirtualKeyModifiers.Shift));

    private void CommandKeys_Invoked(object? sender, PaletteHotkeyEventArgs eventArgs) =>
        DispatcherQueue.TryEnqueue(() => HandleCommandKey(eventArgs.Key, eventArgs.Shift));

    private bool HandleCommandKey(VirtualKey key, bool shiftDown)
    {
        ResetTimeout();
        if (key == VirtualKey.Escape)
        {
            if (_screenshotMode) ShowPrimaryCommands(); else HidePalette();
            return true;
        }
        var command = _screenshotMode ? key switch
        {
            VirtualKey.R => "region",
            VirtualKey.W => "window",
            VirtualKey.D => "display",
            VirtualKey.A => "all-displays",
            VirtualKey.P => "previous-region",
            _ => null,
        } : key switch
        {
            VirtualKey.S => "screenshot",
            VirtualKey.V => shiftDown ? "repeat-video" : "video",
            VirtualKey.A => "audio",
            VirtualKey.T => "text",
            VirtualKey.L => "link",
            VirtualKey.O => "library",
            _ => null,
        };
        if (command is null)
        {
            return false;
        }
        if (command == "screenshot")
        {
            ShowScreenshotCommands();
        }
        else
        {
            Request(command);
        }
        return true;
    }

    private void ShowScreenshotCommands()
    {
        _screenshotMode = true;
        PrimaryCommands.Visibility = Visibility.Collapsed;
        ScreenshotCommands.Visibility = Visibility.Visible;
        PaletteTitle.Text = "Which part of the screen?";
        PaletteHint.Text = "Keys are sequential, never held together";
        RegionCommand.Focus(FocusState.Programmatic);
        ResetTimeout();
    }

    private void ShowPrimaryCommands()
    {
        _screenshotMode = false;
        ScreenshotCommands.Visibility = Visibility.Collapsed;
        PrimaryCommands.Visibility = Visibility.Visible;
        PaletteTitle.Text = "What do you want to catch?";
        PaletteHint.Text = "Press one key · Esc closes";
        ScreenshotCommand.Focus(FocusState.Programmatic);
        ResetTimeout();
    }

    private void Request(string command)
    {
        _timeout.Stop();
        // Library is a persistent surface the user explicitly asked to open;
        // restoring the source after the palette closes would immediately
        // bury it again. Transient capture commands still return to source.
        _restoreSourceOnClose = command != "library";
        if (command == "library")
        {
            CommandRequested?.Invoke(this, command);
            HidePalette();
            return;
        }
        // Release the scoped bare keys (especially Escape) before the next
        // capture surface registers its own cancel key.
        _restoreSourceOnClose = false;
        HidePalette();
        CommandRequested?.Invoke(this, command);
    }

    private void ResetTimeout()
    {
        _timeout.Stop();
        _timeout.Start();
        _lastTimeoutReset = Environment.TickCount64;
    }

    private void HidePalette()
    {
        _timeout.Stop();
        // The palette window stays alive between activations, so a Forever
        // storyboard left running would keep animating an invisible surface.
        PulseStoryboard.Stop();
        _commandKeys.SetEnabled(false);
        AppWindow.Hide();
        if (_restoreSourceOnClose)
        {
            App.Services.Context.RestoreFocus(SourceWindow);
        }
        PaletteHidden?.Invoke(this, EventArgs.Empty);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            // Restarting a DispatcherTimer on every pointer move is pure overhead
            // when the window it guards closes after thirty seconds.
            if (Environment.TickCount64 - _lastTimeoutReset >= 1000)
            {
                ResetTimeout();
            }
            return;
        }
        eventArgs.Handled = true;
        var (pointerX, pointerY) = WindowPlacement.PointerPosition();
        Root_PointerMovedWhileDragging(pointerX, pointerY);
    }

    private void Root_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        _paletteLoaded = true;
        PulseStoryboard.Begin();
        FocusCommandSurface();
    }
    private void FocusCommandSurface() => (_screenshotMode ? RegionCommand : ScreenshotCommand).Focus(FocusState.Programmatic);
    private void Close_Click(object sender, RoutedEventArgs eventArgs) => HidePalette();
    private void LibraryPulse_Click(object sender, RoutedEventArgs eventArgs) => Request("library");
}
