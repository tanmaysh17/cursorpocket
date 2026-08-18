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
    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly PaletteHotkeyService _commandKeys = new();
    private bool _screenshotMode;

    public CommandPaletteWindow(long sourceWindow, string? initialMode = null)
    {
        SourceWindow = sourceWindow;
        InitializeComponent();
        var bounds = WindowPlacement.MonitorUnderPointer();
        BackdropImage.Source = DesktopSnapshot.Capture(bounds);
        WindowPlacement.ConfigureUtilityWindow(this, excludeFromCapture: false);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        _timeout.Tick += (_, _) => ClosePalette();
        _commandKeys.Invoked += CommandKeys_Invoked;
        Closed += (_, _) =>
        {
            _commandKeys.Invoked -= CommandKeys_Invoked;
            _commandKeys.Dispose();
            App.Services.Context.RestoreFocus(SourceWindow);
        };
        Activated += (_, _) =>
        {
            FocusCommandSurface();
            ResetTimeout();
        };
        if (initialMode == "screenshot")
        {
            ShowScreenshotCommands();
        }
        AudioDeviceHint.Text = string.IsNullOrWhiteSpace(App.Services.Settings.VideoMicrophoneName)
            ? "Starts with the default microphone"
            : $"Starts with {App.Services.Settings.VideoMicrophoneName}";
    }

    public long SourceWindow { get; }
    public event EventHandler<string>? CommandRequested;

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
            if (_screenshotMode) ShowPrimaryCommands(); else ClosePalette();
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
        CommandRequested?.Invoke(this, command);
        Close();
    }

    private void ResetTimeout()
    {
        _timeout.Stop();
        _timeout.Start();
    }

    private void ClosePalette()
    {
        _timeout.Stop();
        Close();
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs eventArgs) => ResetTimeout();
    private void Root_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        PulseStoryboard.Begin();
        FocusCommandSurface();
    }
    private void FocusCommandSurface() => (_screenshotMode ? RegionCommand : ScreenshotCommand).Focus(FocusState.Programmatic);
    private void Close_Click(object sender, RoutedEventArgs eventArgs) => ClosePalette();
    private void LibraryPulse_Click(object sender, RoutedEventArgs eventArgs) => Request("library");
}
