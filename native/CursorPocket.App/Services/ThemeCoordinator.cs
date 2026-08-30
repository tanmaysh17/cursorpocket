using System.Drawing;
using CursorPocket.Core.Models;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;
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
    private sealed record GlassProfile(
        double PanelTint,
        double PanelLuminosity,
        double RaisedTint,
        double RaisedLuminosity,
        byte SunkenAlpha,
        byte SurfaceAlpha,
        byte RaisedAlpha,
        byte TransientAlpha);

    private readonly List<Registration> _registrations = [];
    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings _accessibility = new();
    private bool _uiSettingsSubscribed;
    private bool _accessibilitySubscribed;
    private AppThemeMode _mode;
    private GlassTransparencyLevel _glassTransparency;

    public ThemeCoordinator(AppThemeMode mode, GlassTransparencyLevel glassTransparency)
    {
        _mode = mode;
        _glassTransparency = Enum.IsDefined(glassTransparency)
            ? glassTransparency
            : GlassTransparencyLevel.Balanced;
        ApplyGlassTransparency();
        ApplyControlAccents();
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
    }

    public event EventHandler? ThemeChanged;
    public AppThemeMode Mode => _mode;
    public GlassTransparencyLevel GlassTransparency => _glassTransparency;
    public bool IsHighContrast
    {
        get
        {
            try { return _accessibility.HighContrast; }
            catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or NotSupportedException) { return false; }
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
                ColorTranslator.FromHtml("#F7F7F4"),
                ColorTranslator.FromHtml("#FCFCFA"),
                ColorTranslator.FromHtml("#1F2925"),
                ColorTranslator.FromHtml("#4E5C56"),
                ColorTranslator.FromHtml("#5E6B65"),
                ColorTranslator.FromHtml("#C9CEC9"),
                ColorTranslator.FromHtml("#117A46"),
                ColorTranslator.FromHtml("#F9FBFA"),
                false);

    public void SetMode(AppThemeMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        ApplyAll();
    }

    public void SetGlassTransparency(GlassTransparencyLevel glassTransparency)
    {
        glassTransparency = Enum.IsDefined(glassTransparency)
            ? glassTransparency
            : GlassTransparencyLevel.Balanced;
        if (_glassTransparency == glassTransparency) return;
        _glassTransparency = glassTransparency;
        ApplyGlassTransparency();
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
        var glass = ProfileFor(_glassTransparency, palette.IsDark);
        var colour = key switch
        {
            "PocketInk" => palette.Text,
            "PocketInkDim" => palette.InkDim,
            "PocketMuted" => palette.Muted,
            "PocketBase" => palette.Background,
            "PocketSunken" => IsHighContrast
                ? palette.Background
                : WithAlpha(Blend(palette.Background, palette.IsDark ? Color.Black : Color.Gray, 0.12), glass.SunkenAlpha),
            "PocketSurface" => IsHighContrast
                ? palette.Background
                : palette.IsDark ? Color.FromArgb(glass.SurfaceAlpha, 0x10, 0x18, 0x15) : Color.FromArgb(glass.SurfaceAlpha, 0xF9, 0xF9, 0xF6),
            "PocketRaised" => IsHighContrast
                ? palette.Background
                : palette.IsDark ? Color.FromArgb(glass.RaisedAlpha, 0x16, 0x1F, 0x1C) : Color.FromArgb(glass.RaisedAlpha, 0xFC, 0xFC, 0xFA),
            "PocketTransientSurface" => IsHighContrast
                ? palette.Background
                : palette.IsDark ? Color.FromArgb(glass.TransientAlpha, 0x14, 0x1E, 0x1A) : Color.FromArgb(glass.TransientAlpha, 0xF9, 0xF9, 0xF6),
            "PocketLine" => palette.Line,
            "PocketLineStrong" => Blend(palette.Line, palette.Text, 0.20),
            "PocketGreen" => palette.Selection,
            "PocketGreenSoft" => WithAlpha(palette.Selection, 48),
            "PocketOnGreen" => palette.SelectionText,
            "PocketRed" => IsHighContrast ? palette.Selection : ColorTranslator.FromHtml(IsDark ? "#FF5964" : "#C52B3B"),
            "PocketRedSoft" => WithAlpha(ColorTranslator.FromHtml(IsDark ? "#FF5964" : "#C52B3B"), 44),
            "PocketBlue" => IsHighContrast ? palette.Selection : ColorTranslator.FromHtml(IsDark ? "#7AA7FF" : "#24669A"),
            "PocketMediaInk" => IsHighContrast ? palette.Text : ColorTranslator.FromHtml("#F6F4EC"),
            "PocketMediaInkDim" => IsHighContrast ? palette.InkDim : ColorTranslator.FromHtml("#CBD7D1"),
            "PocketMediaMuted" => IsHighContrast ? palette.Muted : ColorTranslator.FromHtml("#AEBDB6"),
            "PocketMediaRaised" => IsHighContrast ? palette.Background : Color.FromArgb(0xED, 0x15, 0x1E, 0x1A),
            "PocketMediaLine" => IsHighContrast ? palette.Line : Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF),
            "PocketMediaGreen" => IsHighContrast ? palette.Selection : ColorTranslator.FromHtml("#36E58C"),
            "PocketMediaRed" => IsHighContrast ? palette.Selection : ColorTranslator.FromHtml("#FF5964"),
            _ => Color.Transparent,
        };
        return new SolidColorBrush(Windows.UI.Color.FromArgb(colour.A, colour.R, colour.G, colour.B));
    }

    /// <summary>
    /// Produces an independently owned pane material. Library panes use this path so
    /// a live transparency change cannot be hidden by a ThemeResource brush that was
    /// resolved before the setting changed.
    /// </summary>
    public Microsoft.UI.Xaml.Media.Brush GlassBrush(bool raised = false)
    {
        if (IsHighContrast)
        {
            return Brush("PocketSurface");
        }

        var dark = IsDark;
        var profile = ProfileFor(_glassTransparency, dark);
        var tint = GlassTintColor(dark, raised);
        var fallback = GlassFallbackColor(dark, raised);
        return new AcrylicBrush
        {
            TintColor = tint,
            TintOpacity = raised ? profile.RaisedTint : profile.PanelTint,
            TintLuminosityOpacity = raised ? profile.RaisedLuminosity : profile.PanelLuminosity,
            FallbackColor = fallback,
        };
    }

    private void ApplyGlassTransparency()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        ApplyGlassProfile(resources, ProfileFor(_glassTransparency, dark: true));
        if (resources.ThemeDictionaries.TryGetValue("Dark", out var darkValue) && darkValue is ResourceDictionary dark)
        {
            ApplyGlassProfile(dark, ProfileFor(_glassTransparency, dark: true));
        }
        if (resources.ThemeDictionaries.TryGetValue("Light", out var lightValue) && lightValue is ResourceDictionary light)
        {
            ApplyGlassProfile(light, ProfileFor(_glassTransparency, dark: false));
        }
    }

    private static GlassProfile ProfileFor(GlassTransparencyLevel level, bool dark) => (level, dark) switch
    {
        (GlassTransparencyLevel.VeryClear, true) => new(0.06, 0.18, 0.18, 0.24, 0x52, 0x38, 0x60, 0x74),
        (GlassTransparencyLevel.VeryClear, false) => new(0.10, 0.24, 0.22, 0.30, 0x64, 0x48, 0x78, 0x88),
        (GlassTransparencyLevel.Clear, true) => new(0.24, 0.42, 0.36, 0.46, 0x78, 0x64, 0x88, 0x98),
        (GlassTransparencyLevel.Clear, false) => new(0.30, 0.50, 0.44, 0.52, 0x8F, 0x78, 0xA0, 0xAE),
        (GlassTransparencyLevel.Solid, true) => new(0.72, 0.88, 0.82, 0.86, 0xD2, 0xC4, 0xE0, 0xE8),
        (GlassTransparencyLevel.Solid, false) => new(0.76, 0.92, 0.86, 0.90, 0xDF, 0xD2, 0xEC, 0xF1),
        (GlassTransparencyLevel.VerySolid, true) => new(0.90, 0.96, 0.94, 0.94, 0xF0, 0xE8, 0xF6, 0xFA),
        (GlassTransparencyLevel.VerySolid, false) => new(0.92, 0.98, 0.96, 0.96, 0xF4, 0xEC, 0xFA, 0xFC),
        (_, true) => new(0.48, 0.72, 0.62, 0.68, 0xA6, 0x8F, 0xB8, 0xC2),
        _ => new(0.54, 0.82, 0.68, 0.78, 0xB8, 0xA8, 0xD1, 0xD9),
    };

    private static void ApplyGlassProfile(ResourceDictionary resources, GlassProfile profile)
    {
        if (resources.ContainsKey("PocketGlassPanel") && resources["PocketGlassPanel"] is AcrylicBrush panel)
        {
            panel.TintOpacity = profile.PanelTint;
            panel.TintLuminosityOpacity = profile.PanelLuminosity;
        }
        if (resources.ContainsKey("PocketGlassRaised") && resources["PocketGlassRaised"] is AcrylicBrush raised)
        {
            raised.TintOpacity = profile.RaisedTint;
            raised.TintLuminosityOpacity = profile.RaisedLuminosity;
        }
        SetBrushAlpha(resources, "PocketSunken", profile.SunkenAlpha);
        SetBrushAlpha(resources, "PocketSurface", profile.SurfaceAlpha);
        SetBrushAlpha(resources, "PocketRaised", profile.RaisedAlpha);
        SetBrushAlpha(resources, "PocketTransientSurface", profile.TransientAlpha);
    }

    private static void SetBrushAlpha(ResourceDictionary resources, string key, byte alpha)
    {
        if (!resources.ContainsKey(key) || resources[key] is not SolidColorBrush brush) return;
        var colour = brush.Color;
        brush.Color = Windows.UI.Color.FromArgb(alpha, colour.R, colour.G, colour.B);
    }

    public System.Windows.Forms.ToolStripRenderer CreateMenuRenderer() => new ThemedToolStripRenderer(Palette);

    private void ApplyAll()
    {
        ApplyControlAccents();
        foreach (var registration in _registrations.ToArray()) Apply(registration);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyControlAccents()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        var palette = Palette;
        var accent = palette.Selection;
        var hover = IsHighContrast ? accent : Blend(accent, palette.Text, 0.08);
        var pressed = IsHighContrast ? accent : Blend(accent, palette.Text, 0.16);
        SetBrushColour(resources, "PocketControlAccent", accent);
        SetBrushColour(resources, "PocketControlAccentHover", hover);
        SetBrushColour(resources, "PocketControlAccentPressed", pressed);
        SetBrushColour(resources, "PocketControlAccentDisabled", WithAlpha(accent, 0x66));
    }

    private static void SetBrushColour(ResourceDictionary resources, string key, Color colour)
    {
        if (!resources.ContainsKey(key) || resources[key] is not SolidColorBrush brush) return;
        brush.Color = ToWindowsColor(colour);
    }

    private void Apply(Registration registration)
    {
        registration.Root.RequestedTheme = _mode switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (registration.Role is SurfaceRole.Pin or SurfaceRole.CaptureOverlay)
        {
            registration.Window.SystemBackdrop = null;
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            var dark = IsDark;
            var profile = ProfileFor(_glassTransparency, dark);
            var isHud = registration.Role == SurfaceRole.Hud;
            var tint = isHud
                ? Windows.UI.Color.FromArgb(0xFF, 0x07, 0x13, 0x0F)
                : GlassTintColor(dark, raised: false);
            var fallback = IsHighContrast
                ? ToWindowsColor(Palette.Background)
                : isHud
                    ? Windows.UI.Color.FromArgb(0xFF, 0x09, 0x11, 0x0F)
                    : GlassFallbackColor(dark, raised: false);
            var tintOpacity = isHud ? 0.84 : profile.PanelTint;
            var luminosityOpacity = isHud ? 0.48 : profile.PanelLuminosity;

            if (registration.Window.SystemBackdrop is PocketAcrylicBackdrop backdrop)
            {
                backdrop.Update(tint, fallback, tintOpacity, luminosityOpacity);
            }
            else
            {
                registration.Window.SystemBackdrop = new PocketAcrylicBackdrop(
                    tint,
                    fallback,
                    tintOpacity,
                    luminosityOpacity);
            }
        }
        else if (registration.Window.SystemBackdrop is not DesktopAcrylicBackdrop)
        {
            // Keep the built-in material as the compatibility path on systems where
            // a configurable controller is unavailable. It still owns the correct
            // opaque policy fallback instead of leaving a transparent window hole.
            registration.Window.SystemBackdrop = new DesktopAcrylicBackdrop();
        }
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

    private static Windows.UI.Color GlassTintColor(bool dark, bool raised) => dark
        ? raised
            ? Windows.UI.Color.FromArgb(0xFF, 0x18, 0x23, 0x1F)
            : Windows.UI.Color.FromArgb(0xFF, 0x10, 0x18, 0x15)
        : raised
            ? Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xFC, 0xFA)
            : Windows.UI.Color.FromArgb(0xFF, 0xF7, 0xF7, 0xF4);

    private static Windows.UI.Color GlassFallbackColor(bool dark, bool raised) => dark
        ? GlassTintColor(dark, raised)
        : raised
            ? Windows.UI.Color.FromArgb(0xFF, 0xFC, 0xFC, 0xFA)
            : GlassTintColor(dark, raised);

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
        _registrations.Clear();
    }
}

/// <summary>
/// Window-level Acrylic whose tint and luminosity can change without replacing the
/// HWND or losing Windows' inactive, disabled-transparency, and high-contrast policy.
/// One instance belongs to one window; ThemeCoordinator never shares it across HWNDs.
/// </summary>
internal sealed class PocketAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private Windows.UI.Color _tintColor;
    private Windows.UI.Color _fallbackColor;
    private float _tintOpacity;
    private float _luminosityOpacity;

    public PocketAcrylicBackdrop(
        Windows.UI.Color tintColor,
        Windows.UI.Color fallbackColor,
        double tintOpacity,
        double luminosityOpacity) =>
        Update(tintColor, fallbackColor, tintOpacity, luminosityOpacity);

    public void Update(
        Windows.UI.Color tintColor,
        Windows.UI.Color fallbackColor,
        double tintOpacity,
        double luminosityOpacity)
    {
        _tintColor = tintColor;
        _fallbackColor = fallbackColor;
        _tintOpacity = (float)Math.Clamp(tintOpacity, 0, 1);
        _luminosityOpacity = (float)Math.Clamp(luminosityOpacity, 0, 1);
        ApplyProperties();
    }

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        if (_controller is not null)
        {
            throw new InvalidOperationException("A PocketAcrylicBackdrop cannot be shared between windows.");
        }

        var controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
        try
        {
            _controller = controller;
            ApplyProperties();
            controller.SetSystemBackdropConfiguration(
                GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
            controller.AddSystemBackdropTarget(connectedTarget);
        }
        catch
        {
            _controller = null;
            controller.Dispose();
            throw;
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_controller is null) return;
        _controller.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller.Dispose();
        _controller = null;
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        // The configuration obtained during OnTargetConnected is maintained by WinUI.
        // Re-querying it from this projected callback can pass an invalid target and
        // crash with E_INVALIDARG while RequestedTheme is changing. Our custom Acrylic
        // values are refreshed here; ThemeCoordinator applies the new palette next.
        ApplyProperties();
    }

    private void ApplyProperties()
    {
        if (_controller is null) return;
        _controller.TintColor = _tintColor;
        _controller.FallbackColor = _fallbackColor;
        _controller.TintOpacity = _tintOpacity;
        _controller.LuminosityOpacity = _luminosityOpacity;
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
