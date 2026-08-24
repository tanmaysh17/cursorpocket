using System.Drawing;
using CursorPocket.Core.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
using Windows.System.Power;
using Color = System.Drawing.Color;

namespace CursorPocket_App.Services;

public enum SurfaceRole
{
    Persistent,
    Workspace,
    Transient,
    Hud,
    Receipt,
    Pin,
    CaptureOverlay,
}

public sealed record ThemePalette(
    System.Drawing.Color Background,
    System.Drawing.Color Raised,
    System.Drawing.Color Text,
    System.Drawing.Color InkDim,
    System.Drawing.Color Muted,
    System.Drawing.Color Line,
    System.Drawing.Color Selection,
    System.Drawing.Color SelectionText,
    bool IsDark);

/// <summary>
/// Owns the app theme across every HWND. XAML theme resources follow each registered
/// root; title bars and the WinForms tray menu need the resolved colours explicitly.
/// </summary>
public sealed class ThemeCoordinator : IDisposable
{
    private sealed record Registration(Window Window, FrameworkElement Root, SurfaceRole Role);

    private readonly List<Registration> _registrations = [];
    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings _accessibility = new();
    private bool _uiSettingsSubscribed;
    private bool _accessibilitySubscribed;
    private bool _powerSubscribed;
    private AppThemeMode _mode;

    public ThemeCoordinator(AppThemeMode mode)
    {
        _mode = mode;
        // These projections are OS-version and session dependent. A missing optional
        // notification must not prevent the app from starting; the current value is
        // still read whenever another supported signal or an app override applies.
        try
        {
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
            _uiSettingsSubscribed = true;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            _uiSettingsSubscribed = false;
        }
        try
        {
            _accessibility.HighContrastChanged += Accessibility_HighContrastChanged;
            _accessibilitySubscribed = true;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            _accessibilitySubscribed = false;
        }
        try
        {
            PowerManager.EnergySaverStatusChanged += PowerManager_EnergySaverStatusChanged;
            _powerSubscribed = true;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            _powerSubscribed = false;
        }
    }

    public event EventHandler? ThemeChanged;
    public AppThemeMode Mode => _mode;
    public bool IsHighContrast
    {
        get
        {
            try { return _accessibility.HighContrast; }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or NotSupportedException) { return false; }
        }
    }
    public bool TransparencyAllowed
    {
        get
        {
            if (IsHighContrast) return false;
            try
            {
                return _uiSettings.AdvancedEffectsEnabled
                    && PowerManager.EnergySaverStatus != EnergySaverStatus.On;
            }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or NotSupportedException)
            {
                return false;
            }
        }
    }
    public bool IsDark => _mode switch
    {
        AppThemeMode.Dark => true,
        AppThemeMode.Light => false,
        _ => IsSystemDark(),
    };

    public ThemePalette Palette => IsHighContrast
        ? HighContrastPalette()
        : IsDark
            ? new ThemePalette(
                ColorTranslator.FromHtml("#141E1A"),
                ColorTranslator.FromHtml("#1C2924"),
                ColorTranslator.FromHtml("#F6F4EC"),
                ColorTranslator.FromHtml("#CBD7D1"),
                ColorTranslator.FromHtml("#8EA099"),
                ColorTranslator.FromHtml("#42504B"),
                ColorTranslator.FromHtml("#36E58C"),
                ColorTranslator.FromHtml("#07130F"),
                true)
            : new ThemePalette(
                ColorTranslator.FromHtml("#F5F9F7"),
                ColorTranslator.FromHtml("#FFFFFF"),
                ColorTranslator.FromHtml("#15201C"),
                ColorTranslator.FromHtml("#35443E"),
                ColorTranslator.FromHtml("#5F6E68"),
                ColorTranslator.FromHtml("#C4CEC9"),
                ColorTranslator.FromHtml("#168B52"),
                Color.White,
                false);

    public void SetMode(AppThemeMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        ApplyAll();
    }

    public void Register(Window window, FrameworkElement root, SurfaceRole role)
    {
        if (_registrations.Any(item => ReferenceEquals(item.Window, window))) return;
        var registration = new Registration(window, root, role);
        _registrations.Add(registration);
        window.Closed += (_, _) => _registrations.RemoveAll(item => ReferenceEquals(item.Window, window));
        Apply(registration);
    }

    public SolidColorBrush Brush(string key)
    {
        var palette = Palette;
        var colour = key switch
        {
            "PocketInk" => palette.Text,
            "PocketInkDim" => palette.InkDim,
            "PocketMuted" => palette.Muted,
            "PocketBase" => palette.Background,
            "PocketSunken" => Blend(palette.Background, palette.IsDark ? Color.Black : Color.Gray, 0.12),
            "PocketSurface" => palette.Background,
            "PocketRaised" => palette.Raised,
            "PocketTransientSurface" => palette.Background,
            "PocketLine" or "PocketLineStrong" => palette.Line,
            "PocketGreen" => palette.Selection,
            "PocketGreenSoft" => WithAlpha(palette.Selection, 48),
            "PocketOnGreen" => palette.SelectionText,
            "PocketRed" => IsHighContrast ? palette.Selection : ColorTranslator.FromHtml(IsDark ? "#FF5964" : "#D73546"),
            "PocketRedSoft" => WithAlpha(ColorTranslator.FromHtml(IsDark ? "#FF5964" : "#D73546"), 44),
            "PocketBlue" => IsHighContrast ? palette.Selection : ColorTranslator.FromHtml(IsDark ? "#7AA7FF" : "#276EA8"),
            _ => Color.Transparent,
        };
        return new SolidColorBrush(Windows.UI.Color.FromArgb(colour.A, colour.R, colour.G, colour.B));
    }

    public System.Windows.Forms.ToolStripRenderer CreateMenuRenderer() => new ThemedToolStripRenderer(Palette);

    private void ApplyAll()
    {
        foreach (var registration in _registrations.ToArray()) Apply(registration);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(Registration registration)
    {
        registration.Root.RequestedTheme = _mode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        registration.Window.SystemBackdrop = !TransparencyAllowed ? null : registration.Role switch
        {
            SurfaceRole.Persistent or SurfaceRole.Workspace => new MicaBackdrop
            {
                Kind = registration.Role == SurfaceRole.Workspace ? MicaKind.BaseAlt : MicaKind.Base,
            },
            SurfaceRole.Transient or SurfaceRole.Hud or SurfaceRole.Receipt => new DesktopAcrylicBackdrop(),
            _ => null,
        };
        ApplyTitleBar(registration.Window.AppWindow.TitleBar);
    }

    private void ApplyTitleBar(AppWindowTitleBar titleBar)
    {
        var palette = Palette;
        var text = ToWindowsColor(palette.Text);
        var muted = ToWindowsColor(palette.Muted);
        var hover = ToWindowsColor(Blend(palette.Background, palette.Text, 0.10));
        titleBar.ButtonForegroundColor = text;
        titleBar.ButtonInactiveForegroundColor = muted;
        titleBar.ButtonBackgroundColor = ToWindowsColor(Color.Transparent);
        titleBar.ButtonInactiveBackgroundColor = ToWindowsColor(Color.Transparent);
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonPressedBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = text;
        titleBar.ButtonPressedForegroundColor = text;
    }

    private void UiSettings_ColorValuesChanged(UISettings sender, object args) => QueueApply();
    private void Accessibility_HighContrastChanged(AccessibilitySettings sender, object args) => QueueApply();
    private void PowerManager_EnergySaverStatusChanged(object? sender, object args) => QueueApply();

    private void QueueApply()
    {
        if (App.DispatcherQueue is null) return;
        App.DispatcherQueue.TryEnqueue(ApplyAll);
    }

    private bool IsSystemDark()
    {
        try
        {
            var background = _uiSettings.GetColorValue(UIColorType.Background);
            return ((background.R * 299) + (background.G * 587) + (background.B * 114)) / 1000 < 128;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or NotSupportedException)
        {
            return false;
        }
    }

    private ThemePalette HighContrastPalette()
    {
        var background = _uiSettings.GetColorValue(UIColorType.Background);
        var foreground = _uiSettings.GetColorValue(UIColorType.Foreground);
        var accent = _uiSettings.GetColorValue(UIColorType.Accent);
        var bg = Color.FromArgb(background.A, background.R, background.G, background.B);
        var fg = Color.FromArgb(foreground.A, foreground.R, foreground.G, foreground.B);
        var hi = Color.FromArgb(accent.A, accent.R, accent.G, accent.B);
        return new ThemePalette(bg, bg, fg, fg, fg, fg, hi, fg, IsSystemDark());
    }

    private static Windows.UI.Color ToWindowsColor(Color colour) =>
        Windows.UI.Color.FromArgb(colour.A, colour.R, colour.G, colour.B);

    private static Color WithAlpha(Color colour, byte alpha) => Color.FromArgb(alpha, colour.R, colour.G, colour.B);
    private static Color Blend(Color a, Color b, double amount) => Color.FromArgb(
        255,
        (int)Math.Round(a.R + ((b.R - a.R) * amount)),
        (int)Math.Round(a.G + ((b.G - a.G) * amount)),
        (int)Math.Round(a.B + ((b.B - a.B) * amount)));

    public void Dispose()
    {
        try { if (_uiSettingsSubscribed) _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged; } catch { }
        try { if (_accessibilitySubscribed) _accessibility.HighContrastChanged -= Accessibility_HighContrastChanged; } catch { }
        try { if (_powerSubscribed) PowerManager.EnergySaverStatusChanged -= PowerManager_EnergySaverStatusChanged; } catch { }
        _registrations.Clear();
    }
}

internal sealed class ThemedToolStripRenderer(ThemePalette palette)
    : System.Windows.Forms.ToolStripProfessionalRenderer(new ThemedColorTable(palette))
{
    protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? palette.Text : palette.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(System.Windows.Forms.ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(palette.Line);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }
}

internal sealed class ThemedColorTable(ThemePalette palette) : System.Windows.Forms.ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => palette.Background;
    public override Color ImageMarginGradientBegin => palette.Background;
    public override Color ImageMarginGradientMiddle => palette.Background;
    public override Color ImageMarginGradientEnd => palette.Background;
    public override Color MenuItemSelected => palette.Raised;
    public override Color MenuItemBorder => palette.Line;
    public override Color MenuItemSelectedGradientBegin => palette.Raised;
    public override Color MenuItemSelectedGradientEnd => palette.Raised;
    public override Color MenuBorder => palette.Line;
    public override Color SeparatorDark => palette.Line;
    public override Color SeparatorLight => palette.Line;
}
