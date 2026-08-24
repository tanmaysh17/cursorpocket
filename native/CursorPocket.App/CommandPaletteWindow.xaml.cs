using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
    private const int RegularWidth = 304;
    private const int RegularHeight = 430;
    private const int ShortWidth = 520;
    private const int ShortHeight = 300;
    private const int ScreenshotWidth = 304;
    private const int ScreenshotHeight = 286;
    private const int PanelMargin = 22;

    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly PaletteHotkeyService _commandKeys = new();
    private (int Left, int Top) _dragOrigin;
    private (int X, int Y) _dragStart;
    private bool _dragging;
    private bool _screenshotMode;
    private bool _restoreSourceOnClose = true;
    private bool _paletteLoaded;
    private bool _twoColumn;
    private long _lastTimeoutReset;
    private readonly List<Button> _primaryButtons = [];
    private readonly List<Button> _screenshotButtons = [];
    private readonly List<TextBlock> _keyLabels = [];
    private readonly List<FontIcon> _kindGlyphs = [];

    public CommandPaletteWindow()
    {
        InitializeComponent();
        App.Theme.Register(this, Root, SurfaceRole.Transient);
        BuildCommands();
        WindowPlacement.ConfigureUtilityWindow(this, excludeFromCapture: false);
        App.Theme.ThemeChanged += Theme_ThemeChanged;
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
        if (initialMode == "screenshot") ShowScreenshotCommands(); else ShowPrimaryCommands();
        ApplySavedPlacement();
        if (_paletteLoaded && App.AnimationsEnabled)
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
        var scale = WindowPlacement.ScaleFor(this);
        var logicalHeight = (work.Bottom - work.Top) / Math.Max(1, scale);
        _twoColumn = !_screenshotMode && logicalHeight < 470;
        ArrangeCommands();
        var desiredWidth = _screenshotMode ? ScreenshotWidth : _twoColumn ? ShortWidth : RegularWidth;
        var desiredHeight = _screenshotMode ? ScreenshotHeight : _twoColumn ? ShortHeight : RegularHeight;
        var layout = TransientWindowLayoutPolicy.Resolve(
            ToBounds(work),
            desiredWidth,
            desiredHeight,
            scale,
            PanelMargin);
        var placement = CommandPanelPlacement.Resolve(
            ToBounds(work),
            layout.Bounds.Width,
            layout.Bounds.Height,
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

    private int PixelMargin() => WindowPlacement.ToPixels(this, PanelMargin);

    public long SourceWindow { get; private set; }
    public event EventHandler<CaptureActionId>? CommandRequested;
    public event EventHandler? PaletteHidden;

    private void Command_Click(object sender, RoutedEventArgs eventArgs)
    {
        ResetTimeout();
        if (sender is not FrameworkElement { Tag: CaptureActionId command })
        {
            return;
        }
        if (command == CaptureActionId.Screenshot)
        {
            ShowScreenshotCommands();
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
        CaptureActionId? command = _screenshotMode ? key switch
        {
            VirtualKey.R => CaptureActionId.Region,
            VirtualKey.W => CaptureActionId.Window,
            VirtualKey.D => CaptureActionId.Display,
            VirtualKey.A => CaptureActionId.AllDisplays,
            VirtualKey.P => CaptureActionId.PreviousRegion,
            _ => null,
        } : key switch
        {
            VirtualKey.S => CaptureActionId.Screenshot,
            VirtualKey.V => shiftDown ? CaptureActionId.RepeatVideo : CaptureActionId.Video,
            VirtualKey.A => CaptureActionId.Audio,
            VirtualKey.T => CaptureActionId.Text,
            VirtualKey.L => CaptureActionId.Link,
            VirtualKey.O => CaptureActionId.Library,
            _ => null,
        };
        if (command is null)
        {
            return false;
        }
        if (command == CaptureActionId.Screenshot)
        {
            ShowScreenshotCommands();
        }
        else
        {
            Request(command.Value);
        }
        return true;
    }

    private void ShowScreenshotCommands()
    {
        _screenshotMode = true;
        PrimaryCommands.Visibility = Visibility.Collapsed;
        ScreenshotCommands.Visibility = Visibility.Visible;
        PaletteTitle.Text = "Which part of the screen?";
        PaletteHint.Text = "Press R, W, D, A, or P · Esc goes back";
        ApplySavedPlacement();
        RegionCommand.Focus(FocusState.Programmatic);
        ResetTimeout();
    }

    private void ShowPrimaryCommands()
    {
        _screenshotMode = false;
        ScreenshotCommands.Visibility = Visibility.Collapsed;
        PrimaryCommands.Visibility = Visibility.Visible;
        PaletteTitle.Text = "What do you want to capture?";
        PaletteHint.Text = "Choose a command or press its key · Esc closes";
        ApplySavedPlacement();
        ScreenshotCommand.Focus(FocusState.Programmatic);
        ResetTimeout();
    }

    private void Request(CaptureActionId command)
    {
        _timeout.Stop();
        // Library is a persistent surface the user explicitly asked to open;
        // restoring the source after the palette closes would immediately
        // bury it again. Transient capture commands still return to source.
        _restoreSourceOnClose = command != CaptureActionId.Library;
        if (command == CaptureActionId.Library)
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
        if (_paletteLoaded) PulseStoryboard.Stop();
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
        if (App.AnimationsEnabled) PulseStoryboard.Begin();
        FocusCommandSurface();
    }
    private void FocusCommandSurface() => (_screenshotMode ? RegionCommand : ScreenshotCommand).Focus(FocusState.Programmatic);
    private void Close_Click(object sender, RoutedEventArgs eventArgs) => HidePalette();
    private void LibraryPulse_Click(object sender, RoutedEventArgs eventArgs) => Request(CaptureActionId.Library);

    private void BuildCommands()
    {
        foreach (var descriptor in CaptureActionCatalog.Primary)
        {
            var button = CreateCommandButton(descriptor, compactTile: false);
            _primaryButtons.Add(button);
            PrimaryCommands.Children.Add(button);
        }
        foreach (var descriptor in CaptureActionCatalog.ScreenshotChoices)
        {
            var button = CreateCommandButton(descriptor, compactTile: true);
            if (descriptor.Id == CaptureActionId.Region) RegionCommand = button;
            _screenshotButtons.Add(button);
            ScreenshotCommands.Children.Add(button);
        }
        var back = new Button
        {
            MinHeight = 68,
            Style = (Style)Application.Current.Resources["PocketCommandRow"],
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = "Esc", FontFamily = new FontFamily("Cascadia Mono"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Back", Style = (Style)Application.Current.Resources["PocketCaptionText"], HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        AutomationProperties.SetName(back, "Back to command mode. Escape.");
        back.Click += (_, _) => ShowPrimaryCommands();
        _screenshotButtons.Add(back);
        ScreenshotCommands.Children.Add(back);
        ScreenshotCommand = (Button)PrimaryCommands.Children[0];
        ArrangeCommands();
        RefreshCommandColors();
    }

    private Button ScreenshotCommand { get; set; } = null!;
    private Button RegionCommand { get; set; } = null!;

    private Button CreateCommandButton(CaptureActionDescriptor descriptor, bool compactTile)
    {
        var button = new Button
        {
            Tag = descriptor.Id,
            MinHeight = compactTile ? 68 : 40,
            Style = (Style)Application.Current.Resources["PocketCommandRow"],
        };
        AutomationProperties.SetName(button, $"{descriptor.Title}. {descriptor.Description}. Key {descriptor.Key}.");
        button.Click += Command_Click;
        if (compactTile)
        {
            var keyLabel = new TextBlock
            {
                Text = descriptor.Key,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsTextScaleFactorEnabled = false,
            };
            _keyLabels.Add(keyLabel);
            button.Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 3,
                Children =
                {
                    keyLabel,
                    new TextBlock
                    {
                        Text = descriptor.Title,
                        Style = (Style)Application.Current.Resources["PocketCaptionText"],
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        MaxLines = 2,
                    },
                },
            };
            return button;
        }

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var key = new TextBlock
        {
            Text = descriptor.Key,
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsTextScaleFactorEnabled = false,
        };
        _keyLabels.Add(key);
        var label = new TextBlock { Text = descriptor.Title, Style = (Style)Application.Current.Resources["PocketBodyStrongText"], VerticalAlignment = VerticalAlignment.Center };
        var glyph = new FontIcon { Glyph = descriptor.Glyph, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        _kindGlyphs.Add(glyph);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(glyph, 2);
        grid.Children.Add(key);
        grid.Children.Add(label);
        grid.Children.Add(glyph);
        button.Content = grid;
        return button;
    }

    private void ArrangeCommands()
    {
        PrimaryCommands.RowDefinitions.Clear();
        PrimaryCommands.ColumnDefinitions.Clear();
        var columns = _twoColumn ? 2 : 1;
        for (var column = 0; column < columns; column++) PrimaryCommands.ColumnDefinitions.Add(new ColumnDefinition());
        var rows = (int)Math.Ceiling(_primaryButtons.Count / (double)columns);
        for (var row = 0; row < rows; row++) PrimaryCommands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < _primaryButtons.Count; index++)
        {
            Grid.SetRow(_primaryButtons[index], index / columns);
            Grid.SetColumn(_primaryButtons[index], index % columns);
        }

        if (ScreenshotCommands.ColumnDefinitions.Count == 0)
        {
            for (var column = 0; column < 3; column++) ScreenshotCommands.ColumnDefinitions.Add(new ColumnDefinition());
            for (var row = 0; row < 2; row++) ScreenshotCommands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var index = 0; index < _screenshotButtons.Count; index++)
            {
                Grid.SetRow(_screenshotButtons[index], index / 3);
                Grid.SetColumn(_screenshotButtons[index], index % 3);
            }
        }
    }

    private void Theme_ThemeChanged(object? sender, EventArgs eventArgs) => RefreshCommandColors();

    private void RefreshCommandColors()
    {
        foreach (var key in _keyLabels) key.Foreground = App.Theme.Brush("PocketGreen");
        foreach (var glyph in _kindGlyphs) glyph.Foreground = App.Theme.Brush("PocketInkDim");
    }
}

