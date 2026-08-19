using System.Diagnostics;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace CursorPocket_App;

public sealed partial class CommandPaletteWindow : Window
{
    private const int PanelWidth = 372;
    private const int PanelHeight = 468;
    private const int PanelMargin = 24;
    private const int PanelRadius = 16;
    // How close the pointer may get before command mode steps aside. Wide enough
    // that the panel clears out before the pointer reaches whatever sits under it.
    private const int KeepAwayPadding = 64;
    private static readonly TimeSpan RelocateCooldown = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly PaletteHotkeyService _commandKeys = new();
    private readonly Stopwatch _sinceRelocated = Stopwatch.StartNew();
    private NativeMethods.Rect _display;
    private PaletteRect _workArea;
    private PaletteRect _panel;
    private PaletteCorner _corner = PaletteCorner.TopRight;
    private bool _visible;
    private bool _keepAwayArmed;
    private bool _screenshotMode;
    private bool _restoreSourceOnClose = true;

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
        _display = WindowPlacement.MonitorUnderPointer();
        _workArea = WindowPlacement.WorkAreaUnderPointer();
        // The snapshot still covers the whole display: the compact panel shows the
        // slice of frozen desktop it happens to cover, and re-slices when it moves.
        BackdropImage.Source = DesktopSnapshot.Capture(_display);
        var (pointerX, pointerY) = WindowPlacement.PointerPosition();
        _corner = PalettePlacementPolicy.ChooseCorner(
            _workArea,
            PixelWidth(),
            PixelHeight(),
            PixelMargin(),
            pointerX,
            pointerY,
            PixelKeepAway());
        // Command mode opens away from the pointer already, so the keep-away rule
        // stays disarmed until the pointer has been clear of the panel once. That
        // stops the panel from hopping the instant it appears.
        _keepAwayArmed = false;
        _sinceRelocated.Restart();
        ApplyPlacement();
        AudioDeviceHint.Text = string.IsNullOrWhiteSpace(App.Services.Settings.VideoMicrophoneName)
            ? "Starts with the default microphone"
            : $"Starts with {App.Services.Settings.VideoMicrophoneName}";
        if (initialMode == "screenshot") ShowScreenshotCommands(); else ShowPrimaryCommands();
        _commandKeys.SetEnabled(true);
        _visible = true;
        AppWindow.Show(false);
        Activate();
    }

    /// <summary>
    /// Steps the panel out of the pointer's way. Called from the shared low-level
    /// mouse hook, so it stays allocation-free and returns early in the common case.
    /// </summary>
    public void NotifyPointerMoved(int x, int y)
    {
        if (!_visible)
        {
            return;
        }
        if (!PalettePlacementPolicy.IsPointerEncroaching(_panel, x, y, PixelKeepAway()))
        {
            _keepAwayArmed = true;
            return;
        }
        // Hold still once the pointer is actually on the panel, and move at most
        // once per approach, so the command rows stay clickable with the mouse
        // instead of fleeing the pointer that is trying to reach them.
        if (!_keepAwayArmed || _panel.Contains(x, y) || _sinceRelocated.Elapsed < RelocateCooldown)
        {
            return;
        }
        _corner = PalettePlacementPolicy.ChooseCorner(
            _workArea,
            PixelWidth(),
            PixelHeight(),
            PixelMargin(),
            x,
            y,
            PixelKeepAway(),
            avoid: _corner);
        _keepAwayArmed = false;
        _sinceRelocated.Restart();
        ApplyPlacement();
    }

    private void ApplyPlacement()
    {
        _panel = PalettePlacementPolicy.RectFor(_corner, _workArea, PixelWidth(), PixelHeight(), PixelMargin());
        WindowPlacement.MoveAndResizeTo(this, _panel);
        WindowPlacement.ClipToRoundedPixelRegion(this, _panel.Width, _panel.Height, WindowPlacement.ToPixels(this, PanelRadius));
        AlignBackdrop();
    }

    private void AlignBackdrop()
    {
        var scale = WindowPlacement.ScaleFor(this);
        BackdropImage.Width = (_display.Right - _display.Left) / scale;
        BackdropImage.Height = (_display.Bottom - _display.Top) / scale;
        BackdropOffset.X = -(_panel.Left - _display.Left) / scale;
        BackdropOffset.Y = -(_panel.Top - _display.Top) / scale;
    }

    private int PixelWidth() => WindowPlacement.ToPixels(this, PanelWidth);
    private int PixelHeight() => WindowPlacement.ToPixels(this, PanelHeight);
    private int PixelMargin() => WindowPlacement.ToPixels(this, PanelMargin);
    private int PixelKeepAway() => WindowPlacement.ToPixels(this, KeepAwayPadding);

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
        PaletteHint.Text = "Press S, then one of these keys. They are sequential, never held together.";
        RegionCommand.Focus(FocusState.Programmatic);
        ResetTimeout();
    }

    private void ShowPrimaryCommands()
    {
        _screenshotMode = false;
        ScreenshotCommands.Visibility = Visibility.Collapsed;
        PrimaryCommands.Visibility = Visibility.Visible;
        PaletteTitle.Text = "What do you want to catch?";
        PaletteHint.Text = "Press one key. Nothing here affects normal typing.";
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
    }

    private void HidePalette()
    {
        _timeout.Stop();
        _commandKeys.SetEnabled(false);
        _visible = false;
        AppWindow.Hide();
        if (_restoreSourceOnClose)
        {
            App.Services.Context.RestoreFocus(SourceWindow);
        }
        PaletteHidden?.Invoke(this, EventArgs.Empty);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs eventArgs) => ResetTimeout();
    private void Root_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        PulseStoryboard.Begin();
        FocusCommandSurface();
    }
    private void FocusCommandSurface() => (_screenshotMode ? RegionCommand : ScreenshotCommand).Focus(FocusState.Programmatic);
    private void Close_Click(object sender, RoutedEventArgs eventArgs) => HidePalette();
    private void LibraryPulse_Click(object sender, RoutedEventArgs eventArgs) => Request("library");
}
