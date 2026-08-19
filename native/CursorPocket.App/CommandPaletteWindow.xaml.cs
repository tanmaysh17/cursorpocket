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
    // Command mode always opens in the same place. Moving it around — even to keep
    // clear of the pointer — cost more in predictability than it bought in room.
    private const int PanelWidth = 296;
    private const int PanelHeight = 340;
    private const int PanelMargin = 22;
    private const int PanelRadius = 14;

    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly PaletteHotkeyService _commandKeys = new();
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
        // Same corner of the pointer's display every time, so the panel is where
        // the user already expects it. Acrylic keeps the source readable behind it,
        // which is what the full-screen desktop snapshot used to be for.
        WindowPlacement.PlaceTopRight(this, PanelWidth, PanelHeight, PanelMargin);
        WindowPlacement.ClipToRoundedRegion(this, PanelWidth, PanelHeight, PanelRadius);
        PaletteHint.Text = string.IsNullOrWhiteSpace(App.Services.Settings.VideoMicrophoneName)
            ? "Press one key · Esc closes"
            : $"A uses {App.Services.Settings.VideoMicrophoneName} · Esc closes";
        if (initialMode == "screenshot") ShowScreenshotCommands(); else ShowPrimaryCommands();
        _commandKeys.SetEnabled(true);
        AppWindow.Show(false);
        Activate();
    }

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
    }

    private void HidePalette()
    {
        _timeout.Stop();
        _commandKeys.SetEnabled(false);
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
