using CursorPocket.Core.Annotations;
using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.System;
using Line = Microsoft.UI.Xaml.Shapes.Line;
using Point = Windows.Foundation.Point;
using Polygon = Microsoft.UI.Xaml.Shapes.Polygon;
using Polyline = Microsoft.UI.Xaml.Shapes.Polyline;

// Microsoft.UI.Xaml.Shapes is deliberately not imported wholesale: its Path collides
// with System.IO.Path, which this file also needs. The shapes it does use are aliased.
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace CursorPocket_App;

public sealed partial class AnnotationWindow : Window
{
    /// <summary>
    /// Chaikin passes over a freehand stroke. Two is enough to read as a drawn line;
    /// more only multiplies points.
    /// </summary>
    private const int SmoothingPasses = 2;

    /// <summary>
    /// Toolbar width thresholds in dips. At 46 px a button plus its engraved key, the
    /// full toolbar needs roughly 1060 dips — more than a 1920 display gives at 150%
    /// scale. Below each threshold the teaching degrades; no tool is ever hidden.
    /// </summary>
    private const double CompactToolbarWidth = 1060;

    private const double TightToolbarWidth = 900;

    private readonly CaptureRecord _record;
    private readonly string _path;
    private readonly AnnotationHistory _history = new();
    private readonly Dictionary<int, UIElement> _visuals = [];
    private readonly List<Button> _toolButtons = [];
    private readonly List<Button> _swatchButtons = [];
    private readonly IDisposable _escapeLease;

    /// <summary>
    /// The source pixels, decoded once and held for the session. The first version set
    /// the Image source straight from the file path and re-read that same path at save
    /// time, then moved a temporary file over it — reading and overwriting one file.
    /// Holding the bitmap also gives the eyedropper, redaction, and OCR their pixels.
    /// </summary>
    private readonly System.Drawing.Bitmap _source;

    private readonly int _sourceWidth;
    private readonly int _sourceHeight;

    private AnnotationTool _tool = AnnotationTool.Arrow;
    private AnnotationInk _ink = AnnotationPalette.Default;
    private AnnotationSizeStep _size = AnnotationSizeStep.Medium;
    private bool _boxFilled;
    private bool _ellipseFilled;
    private bool _finished;

    // The gesture in flight. Strokes append to their visual as the pointer moves;
    // everything else rebuilds its visual from the mark, so what is on screen mid-drag
    // is exactly what redo would rebuild.
    private bool _dragging;
    private Point _press;
    private Point _current;
    private List<AnnPoint>? _strokePoints;
    private Polyline? _strokeVisual;

    public AnnotationWindow(CaptureRecord record, string path)
    {
        _record = record;
        _path = path;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AnnotationTitleBar);

        var bounds = WindowPlacement.MonitorUnderPointer(true);
        AppWindow.MoveAndResize(new RectInt32(
            bounds.Left + 20,
            bounds.Top + 20,
            Math.Max(760, bounds.Right - bounds.Left - 40),
            Math.Max(560, bounds.Bottom - bounds.Top - 40)));

        _source = LoadSource(path, out var displaySource);
        _sourceWidth = _source.Width;
        _sourceHeight = _source.Height;

        // The stage is sized to the source bitmap, so a canvas coordinate is an image
        // pixel. Nudging, the native-pixel readout, and a faithful export all rest on
        // that identity; the Viewbox above only scales it to fit the window.
        Stage.Width = _sourceWidth;
        Stage.Height = _sourceHeight;
        DrawingSurface.Width = _sourceWidth;
        DrawingSurface.Height = _sourceHeight;
        ScreenshotImage.Source = displaySource;

        SourceNameText.Text = $"{Path.GetFileName(path)} · {_sourceWidth} × {_sourceHeight}";

        BuildSwatches();
        BuildKeySheet();
        RegisterOemAccelerators();
        CollectToolButtons();
        ApplyToolState();
        ApplyHistoryState();

        Root.SizeChanged += (_, args) => ApplyToolbarWidth(args.NewSize.Width);

        _escapeLease = App.Services.EscapeHotkey.Capture(() => DispatcherQueue.TryEnqueue(HandleEscape));
        // Focus has to land on Loaded, not on Activated: Activated fires before the
        // content tree exists, so Focus() there silently returns false and leaves the
        // window with nothing focused — which is what kept every accelerator dead until
        // the user happened to click a toolbar button.
        CanvasHost.Loaded += (_, _) => CanvasHost.Focus(FocusState.Programmatic);
        Activated += (_, args) =>
        {
            // XamlRoot does not exist yet on the first activation, and FocusManager
            // throws rather than returning null when handed a null one.
            if (args.WindowActivationState == WindowActivationState.Deactivated
                || Content?.XamlRoot is null)
            {
                return;
            }

            // Reclaim focus on re-activation, but never take it from the inline text
            // editor: alt-tabbing away mid-sentence and back must not lose the caret.
            if (FocusManager.GetFocusedElement(Content.XamlRoot) is null)
            {
                CanvasHost.Focus(FocusState.Programmatic);
            }
        };
        Closed += (_, _) =>
        {
            _escapeLease.Dispose();
            _source.Dispose();
            if (!_finished)
            {
                Cancelled?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? Saved;

    public event EventHandler? Cancelled;

    /// <summary>Raised when the marked-up image should be put on the clipboard as-is.</summary>
    public event EventHandler? CopyRequested;

    // ---------------------------------------------------------------- source loading

    /// <summary>
    /// Reads the file once into memory and hands back both an independent GDI+ bitmap
    /// for export and a WinUI source for the preview. Neither holds the file open, so
    /// saving can move a new file over it.
    /// </summary>
    private static System.Drawing.Bitmap LoadSource(string path, out ImageSource displaySource)
    {
        var bytes = File.ReadAllBytes(path);

        System.Drawing.Bitmap bitmap;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var decoded = System.Drawing.Image.FromStream(stream);
            bitmap = new System.Drawing.Bitmap(decoded);
        }
        catch (Exception error) when (error is ArgumentException or OutOfMemoryException)
        {
            // GDI+ raises a bare OutOfMemoryException for a malformed image, which reads
            // as a resource problem and gets misdiagnosed. Say what actually happened.
            throw new InvalidOperationException($"'{Path.GetFileName(path)}' could not be opened as an image.", error);
        }

        var image = new BitmapImage();
        using (var winrt = new Windows.Storage.Streams.InMemoryRandomAccessStream())
        {
            using (var writer = new Windows.Storage.Streams.DataWriter(winrt))
            {
                writer.WriteBytes(bytes);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }

            winrt.Seek(0);
            image.SetSource(winrt);
        }

        displaySource = image;
        return bitmap;
    }

    // ------------------------------------------------------------------ toolbar state

    private void CollectToolButtons()
    {
        _toolButtons.AddRange([SelectTool, ArrowTool, LineTool, PenTool, HighlightTool, BoxTool, EllipseTool, TextTool]);
    }

    private void BuildSwatches()
    {
        for (var index = 0; index < AnnotationPalette.Inks.Count; index++)
        {
            var ink = AnnotationPalette.Inks[index];
            var key = (index + 1).ToString();
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["PocketSwatchButton"],
                Tag = ink,
            };
            ToolTipService.SetToolTip(button, $"{ink.Name} · {key}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{ink.Name} ink");

            var stack = new StackPanel { Spacing = 1 };
            stack.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 3, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Fill = new SolidColorBrush(ToWinUi(ink.Colour)),
            });
            stack.Children.Add(new TextBlock
            {
                Text = key,
                FontFamily = new FontFamily("Cascadia Mono"),
                FontSize = 10,
                IsTextScaleFactorEnabled = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["PocketMuted"],
            });

            button.Content = stack;
            button.Click += Swatch_Click;
            _swatchButtons.Add(button);
            SwatchStrip.Children.Add(button);
        }
    }

    private void ApplyToolState()
    {
        var green = (Brush)Application.Current.Resources["PocketGreen"];
        var greenSoft = (Brush)Application.Current.Resources["PocketGreenSoft"];
        var dim = (Brush)Application.Current.Resources["PocketInkDim"];
        var muted = (Brush)Application.Current.Resources["PocketMuted"];
        var clear = (Brush)Application.Current.Resources["PocketTransparent"];

        foreach (var button in _toolButtons)
        {
            var active = button.Tag is string tag
                && Enum.TryParse<AnnotationTool>(tag, out var tool)
                && tool == _tool;

            // Green is legitimate here because the active tool IS the current selection,
            // one of the four things DESIGN.md allows it on. It stays a soft tint plus a
            // hairline; the only solid green on this surface is the Save button.
            button.Background = active ? greenSoft : clear;
            button.BorderBrush = active ? green : clear;
            PaintToolButton(button, active ? green : dim, active ? green : muted);
        }

        foreach (var button in _swatchButtons)
        {
            var active = ReferenceEquals(button.Tag, _ink);
            // Deliberately not green: green already means the active tool here, and two
            // competing selection greens in one toolbar would make neither readable.
            button.BorderBrush = active ? (Brush)Application.Current.Resources["PocketInk"] : clear;
            button.Background = active ? (Brush)Application.Current.Resources["PocketRaised"] : clear;
            if (button.Content is StackPanel { Children: [_, TextBlock numeral] })
            {
                numeral.Foreground = active ? (Brush)Application.Current.Resources["PocketInk"] : muted;
            }
        }

        BoxFillGlyph.Visibility = _boxFilled ? Visibility.Visible : Visibility.Collapsed;
        EllipseFillGlyph.Visibility = _ellipseFilled ? Visibility.Visible : Visibility.Collapsed;
        SizeStepText.Text = _size switch
        {
            AnnotationSizeStep.Small => "S",
            AnnotationSizeStep.Large => "L",
            _ => "M",
        };

        StatusToolText.Text = DescribeTool();
    }

    /// <summary>
    /// Recolours a tool button's glyph and engraved key. Reaches the two elements
    /// through the button's own Content rather than the visual tree, so it works before
    /// the template is realized and needs no x:Name per icon.
    /// </summary>
    private static void PaintToolButton(Button button, Brush glyph, Brush key)
    {
        if (button.Content is not Grid grid)
        {
            return;
        }

        foreach (var child in grid.Children)
        {
            switch (child)
            {
                case ShapePath path:
                    ApplyGlyphBrush(path, glyph);
                    break;
                case Grid nested:
                    foreach (var inner in nested.Children.OfType<ShapePath>())
                    {
                        ApplyGlyphBrush(inner, glyph);
                    }
                    break;
                case TextBlock text:
                    text.Foreground = key;
                    break;
            }
        }
    }

    /// <summary>
    /// A filled icon carries its colour in Fill and a stroked one in Stroke. Setting
    /// both would give a filled glyph an outline it was not drawn with.
    /// </summary>
    private static void ApplyGlyphBrush(ShapePath path, Brush brush)
    {
        if (path.Fill is not null)
        {
            path.Fill = brush;
        }
        else
        {
            path.Stroke = brush;
        }
    }

    private string DescribeTool() => _tool switch
    {
        AnnotationTool.Select => "SELECT · nothing to pick yet",
        AnnotationTool.Arrow => "ARROW",
        AnnotationTool.Line => "LINE · shift snaps to 45°",
        AnnotationTool.Pen => "PEN",
        AnnotationTool.Highlight => "HIGHLIGHTER",
        AnnotationTool.Box => _boxFilled ? "BOX · FILLED · R again for hollow" : "BOX · HOLLOW · R again for filled",
        AnnotationTool.Ellipse => _ellipseFilled ? "ELLIPSE · FILLED · E again for hollow" : "ELLIPSE · HOLLOW · E again for filled",
        AnnotationTool.Text => "TEXT · type, then Enter",
        _ => _tool.ToString().ToUpperInvariant(),
    };

    private void ApplyHistoryState()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
    }

    private void ApplyToolbarWidth(double width)
    {
        var showKeys = width >= CompactToolbarWidth;
        var buttonWidth = width >= CompactToolbarWidth ? 46d : 36d;

        foreach (var button in _toolButtons.Append(SizeButton))
        {
            button.Width = buttonWidth;
            if (button.Content is Grid grid)
            {
                foreach (var text in grid.Children.OfType<TextBlock>())
                {
                    text.Visibility = showKeys ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        // The last thing to go is a label on a button that has an icon anyway. Every
        // tool stays reachable at every width.
        KeepOriginalButton.Content = width >= TightToolbarWidth ? "Keep original" : "Keep";
        foreach (var button in _swatchButtons)
        {
            button.Width = width >= TightToolbarWidth ? 26 : 22;
        }
    }

    // ------------------------------------------------------------------- tool changes

    private void Tool_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string tag } && Enum.TryParse<AnnotationTool>(tag, out var tool))
        {
            SelectTool_(tool);
        }
    }

    /// <summary>
    /// Arms a tool, or cycles its variant when it is already armed. Pressing the same
    /// key again is how a variant is reached, so the toolbar needs no hover submenu and
    /// the status strip can always say what the next press will do.
    /// </summary>
    private void SelectTool_(AnnotationTool tool)
    {
        if (_tool == tool)
        {
            switch (tool)
            {
                case AnnotationTool.Box:
                    _boxFilled = !_boxFilled;
                    break;
                case AnnotationTool.Ellipse:
                    _ellipseFilled = !_ellipseFilled;
                    break;
            }
        }

        _tool = tool;
        ApplyToolState();
    }

    private void Swatch_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: AnnotationInk ink })
        {
            _ink = ink;
            ApplyToolState();
        }
    }

    private void Size_Click(object sender, RoutedEventArgs eventArgs) => StepSize(1);

    private void StepSize(int direction)
    {
        var next = AnnotationMetrics.Step(_size, direction);
        if (next == _size && direction > 0)
        {
            // Cycle round from the toolbar button, which has no direction of its own.
            next = AnnotationSizeStep.Small;
        }

        _size = next;
        ApplyToolState();
    }

    // -------------------------------------------------------------------- drawing

    private double CurrentStrokeWidth => _tool == AnnotationTool.Highlight
        ? AnnotationMetrics.HighlightWidth(_sourceWidth, _sourceHeight, _size)
        : AnnotationMetrics.StrokeWidth(_sourceWidth, _sourceHeight, _size);

    private static DrawModifiers CurrentModifiers()
    {
        var modifiers = DrawModifiers.None;
        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= DrawModifiers.Constrain;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= DrawModifiers.CenterOnPress;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    private void DrawingSurface_PointerPressed(object sender, PointerRoutedEventArgs eventArgs)
    {
        _press = eventArgs.GetCurrentPoint(DrawingSurface).Position;
        _current = _press;

        if (_tool == AnnotationTool.Text)
        {
            BeginText(_press);
            return;
        }

        if (_tool == AnnotationTool.Select)
        {
            // Nothing to select yet: marks are not yet individually addressable. The
            // status strip says so rather than the click doing nothing unexplained.
            return;
        }

        DrawingSurface.CapturePointer(eventArgs.Pointer);
        _dragging = true;

        if (_tool is AnnotationTool.Pen or AnnotationTool.Highlight)
        {
            _strokePoints = [ToAnn(_press)];
            _strokeVisual = new Polyline
            {
                Stroke = new SolidColorBrush(ToWinUi(StrokeColour())),
                StrokeThickness = CurrentStrokeWidth,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            _strokeVisual.Points.Add(_press);
            DrawingSurface.Children.Add(_strokeVisual);
        }

        UpdateReadout();
    }

    private void DrawingSurface_PointerMoved(object sender, PointerRoutedEventArgs eventArgs)
    {
        _current = eventArgs.GetCurrentPoint(DrawingSurface).Position;
        UpdateReadout();

        if (!_dragging)
        {
            return;
        }

        if (_strokeVisual is not null && _strokePoints is not null)
        {
            // A stroke appends rather than rebuilding: rebuilding a growing polyline on
            // every pointer move would be quadratic over a long stroke.
            _strokePoints.Add(ToAnn(_current));
            _strokeVisual.Points.Add(_current);
            return;
        }

        RebuildPreview();
    }

    private void DrawingSurface_PointerReleased(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _current = eventArgs.GetCurrentPoint(DrawingSurface).Position;
        DrawingSurface.ReleasePointerCapture(eventArgs.Pointer);
        ClearPreview();

        var mark = BuildMark();
        if (mark is not null)
        {
            Commit(mark);
        }

        _strokePoints = null;
        _strokeVisual = null;
        UpdateReadout();
    }

    private void DrawingSurface_PointerExited(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            StatusPointerText.Text = string.Empty;
        }
    }

    /// <summary>
    /// Turns the gesture in flight into a mark. Every shape's geometry comes from
    /// AnnotationGeometry, which is also what the exporter uses, so the preview and the
    /// saved file cannot describe different shapes.
    /// </summary>
    private AnnotationMark? BuildMark()
    {
        var id = _history.AllocateId();
        var colour = StrokeColour();
        var width = CurrentStrokeWidth;
        var modifiers = CurrentModifiers();

        switch (_tool)
        {
            case AnnotationTool.Pen:
            case AnnotationTool.Highlight:
            {
                if (_strokePoints is null || _strokePoints.Count < 2)
                {
                    return null;
                }

                var spacing = AnnotationMetrics.StrokeSampleSpacing(_sourceWidth, _sourceHeight);
                var thinned = AnnotationGeometry.Decimate(_strokePoints, spacing);
                var smoothed = AnnotationGeometry.Smooth(thinned, SmoothingPasses);
                return new StrokeMark
                {
                    Id = id,
                    Colour = colour,
                    StrokeWidth = width,
                    Points = smoothed,
                    Highlight = _tool == AnnotationTool.Highlight,
                };
            }

            case AnnotationTool.Arrow:
            {
                var end = EndPoint(modifiers);
                if ((end - ToAnn(_press)).Length < 2)
                {
                    return null;
                }

                return new ArrowMark { Id = id, Colour = colour, StrokeWidth = width, Start = ToAnn(_press), End = end };
            }

            case AnnotationTool.Line:
            {
                var end = EndPoint(modifiers);
                if ((end - ToAnn(_press)).Length < 2)
                {
                    return null;
                }

                return new LineMark { Id = id, Colour = colour, StrokeWidth = width, Start = ToAnn(_press), End = end };
            }

            case AnnotationTool.Box:
            {
                var rect = CurrentRect(modifiers);
                return rect.Width < 2 || rect.Height < 2
                    ? null
                    : new BoxMark { Id = id, Colour = colour, StrokeWidth = width, Rect = rect, Filled = _boxFilled };
            }

            case AnnotationTool.Ellipse:
            {
                var rect = CurrentRect(modifiers);
                return rect.Width < 2 || rect.Height < 2
                    ? null
                    : new EllipseMark { Id = id, Colour = colour, StrokeWidth = width, Rect = rect, Filled = _ellipseFilled };
            }

            default:
                return null;
        }
    }

    private AnnPoint EndPoint(DrawModifiers modifiers) => modifiers.HasFlag(DrawModifiers.Constrain)
        ? AnnotationGeometry.ConstrainToAngle(ToAnn(_press), ToAnn(_current))
        : ToAnn(_current);

    private AnnRect CurrentRect(DrawModifiers modifiers) => AnnotationGeometry.ClampToImage(
        AnnotationGeometry.RectFromDrag(ToAnn(_press), ToAnn(_current), modifiers),
        _sourceWidth,
        _sourceHeight);

    private AnnColor StrokeColour() => _tool == AnnotationTool.Highlight
        ? _ink.Colour.WithAlpha(AnnotationPalette.HighlightAlpha)
        : _ink.Colour;

    private void Commit(AnnotationMark mark)
    {
        // Adding discards whatever was ahead of the index, so any element still drawing
        // an undone mark has to go with it — otherwise a redone-then-replaced mark would
        // linger on screen with nothing in history to account for it.
        _history.Add(mark);
        PruneStaleVisuals();
        AddVisual(mark);
        ApplyHistoryState();
    }

    private void PruneStaleVisuals()
    {
        var live = _history.Visible.Select(mark => mark.Id).ToHashSet();
        foreach (var id in _visuals.Keys.Where(id => !live.Contains(id)).ToList())
        {
            if (_visuals.Remove(id, out var visual))
            {
                DrawingSurface.Children.Remove(visual);
            }
        }
    }

    private void AddVisual(AnnotationMark mark)
    {
        var visual = CreateVisual(mark);
        _visuals[mark.Id] = visual;
        DrawingSurface.Children.Add(visual);
    }

    // ------------------------------------------------------------------ preview shapes

    private UIElement? _preview;

    private void RebuildPreview()
    {
        ClearPreview();
        var mark = BuildMark();
        if (mark is null)
        {
            return;
        }

        // The provisional mark is discarded, so its identity must not be spent.
        _preview = CreateVisual(mark);
        DrawingSurface.Children.Add(_preview);
    }

    private void ClearPreview()
    {
        if (_preview is not null)
        {
            DrawingSurface.Children.Remove(_preview);
            _preview = null;
        }
    }

    /// <summary>
    /// The one place a mark becomes a WinUI element. Its geometry comes from the same
    /// Core helpers the exporter uses, so the two cannot disagree about shape.
    /// </summary>
    private UIElement CreateVisual(AnnotationMark mark)
    {
        var brush = new SolidColorBrush(ToWinUi(mark.Colour));
        switch (mark)
        {
            case ArrowMark arrow:
            {
                // A filled polygon, not a stroked line with an arrow cap: cap styles
                // differ per renderer and scale with pen width in renderer-specific
                // ways, which is exactly how the drawn and saved arrows diverged.
                var polygon = new Polygon { Fill = brush };
                foreach (var point in AnnotationGeometry.ArrowOutline(arrow.Start, arrow.End, arrow.StrokeWidth))
                {
                    polygon.Points.Add(ToXaml(point));
                }

                return polygon;
            }

            case LineMark line:
                return new Line
                {
                    X1 = line.Start.X,
                    Y1 = line.Start.Y,
                    X2 = line.End.X,
                    Y2 = line.End.Y,
                    Stroke = brush,
                    StrokeThickness = line.StrokeWidth,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };

            case StrokeMark stroke:
            {
                var polyline = new Polyline
                {
                    Stroke = brush,
                    StrokeThickness = stroke.StrokeWidth,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                foreach (var point in stroke.Points)
                {
                    polyline.Points.Add(ToXaml(point));
                }

                return polyline;
            }

            case BoxMark box:
            {
                var rectangle = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width = box.Rect.Width,
                    Height = box.Rect.Height,
                    Stroke = brush,
                    StrokeThickness = box.StrokeWidth,
                    // Filled means filled in the file too. The old surface previewed a
                    // faint fill on every box and exported none of it.
                    Fill = box.Filled ? new SolidColorBrush(ToWinUi(box.Colour.WithAlpha(64))) : null,
                };
                Canvas.SetLeft(rectangle, box.Rect.X);
                Canvas.SetTop(rectangle, box.Rect.Y);
                return rectangle;
            }

            case EllipseMark ellipse:
            {
                var shape = new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = ellipse.Rect.Width,
                    Height = ellipse.Rect.Height,
                    Stroke = brush,
                    StrokeThickness = ellipse.StrokeWidth,
                    Fill = ellipse.Filled ? new SolidColorBrush(ToWinUi(ellipse.Colour.WithAlpha(64))) : null,
                };
                Canvas.SetLeft(shape, ellipse.Rect.X);
                Canvas.SetTop(shape, ellipse.Rect.Y);
                return shape;
            }

            case TextMark text:
            {
                var label = new TextBlock
                {
                    Text = text.Text,
                    FontSize = text.FontSize,
                    FontFamily = new FontFamily(AnnotationExport.TextFamily),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = brush,
                };
                Canvas.SetLeft(label, text.Anchor.X);
                Canvas.SetTop(label, text.Anchor.Y);
                return label;
            }

            default:
                throw new NotSupportedException($"No visual for {mark.GetType().Name}.");
        }
    }

    // ---------------------------------------------------------------------- text tool

    private void BeginText(Point point)
    {
        var fontSize = AnnotationMetrics.TextSize(_sourceWidth, _sourceHeight, _size);
        var editor = new TextBox
        {
            MinWidth = Math.Max(240, fontSize * 8),
            FontSize = fontSize,
            FontFamily = new FontFamily(AnnotationExport.TextFamily),
            Foreground = new SolidColorBrush(ToWinUi(_ink.Colour)),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(210, 8, 14, 12)),
            PlaceholderText = "Type, then press Enter",
        };
        Canvas.SetLeft(editor, point.X);
        Canvas.SetTop(editor, point.Y);
        DrawingSurface.Children.Add(editor);
        editor.Focus(FocusState.Programmatic);

        editor.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Enter)
            {
                return;
            }

            args.Handled = true;
            var value = editor.Text.Trim();
            DrawingSurface.Children.Remove(editor);
            if (value.Length > 0)
            {
                Commit(new TextMark
                {
                    Id = _history.AllocateId(),
                    Colour = _ink.Colour,
                    StrokeWidth = CurrentStrokeWidth,
                    Anchor = ToAnn(point),
                    Text = value,
                    FontSize = fontSize,
                });
            }

            CanvasHost.Focus(FocusState.Programmatic);
        };
    }

    // ------------------------------------------------------------------------ readout

    /// <summary>
    /// The ruler. Idle it reports where the pointer is; mid-drag it reports the size of
    /// the mark in the pixels the export will actually contain.
    /// </summary>
    private void UpdateReadout()
    {
        StatusPointerText.Text = $"x {(int)Math.Round(_current.X)}  y {(int)Math.Round(_current.Y)}";

        if (!_dragging)
        {
            StatusSizeText.Text = string.Empty;
            return;
        }

        if (_tool is AnnotationTool.Arrow or AnnotationTool.Line)
        {
            var end = EndPoint(CurrentModifiers());
            StatusSizeText.Text = $"{(int)Math.Round((end - ToAnn(_press)).Length)} px";
            return;
        }

        if (_tool is AnnotationTool.Pen or AnnotationTool.Highlight)
        {
            StatusSizeText.Text = string.Empty;
            return;
        }

        var rect = CurrentRect(CurrentModifiers());
        StatusSizeText.Text = $"{(int)Math.Round(rect.Width)} × {(int)Math.Round(rect.Height)}";
    }

    // ------------------------------------------------------------------- history keys

    private void Undo_Click(object sender, RoutedEventArgs eventArgs) => Undo();

    private void Redo_Click(object sender, RoutedEventArgs eventArgs) => Redo();

    private void Undo()
    {
        var mark = _history.Undo();
        if (mark is null)
        {
            return;
        }

        if (_visuals.Remove(mark.Id, out var visual))
        {
            DrawingSurface.Children.Remove(visual);
        }

        ApplyHistoryState();
    }

    private void Redo()
    {
        var mark = _history.Redo();
        if (mark is null)
        {
            return;
        }

        // Rebuilt from the mark, which is only possible because a mark is data rather
        // than a live element. The old surface could not offer redo for that reason.
        AddVisual(mark);
        ApplyHistoryState();
    }

    // ----------------------------------------------------------------------- key sheet

    private void KeySheet_Click(object sender, RoutedEventArgs eventArgs) => ToggleKeySheet();

    private void ToggleKeySheet() =>
        KeySheet.Visibility = KeySheet.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Only the modifiers go here. Every tool key is already engraved on its own button,
    /// so nothing behind this panel is needed for a first successful edit.
    /// </summary>
    private void BuildKeySheet()
    {
        (string Key, string Meaning)[] left =
        [
            ("Shift", "square, circle, or 45° line"),
            ("Alt", "centre the shape on the press point"),
            ("[  ]", "smaller or larger marks"),
            ("1 – 6", "ink colour"),
        ];
        (string Key, string Meaning)[] right =
        [
            ("Ctrl+Z", "undo"),
            ("Ctrl+Y", "redo"),
            ("Ctrl+C", "copy without saving"),
            ("Enter", "save · Esc keeps the original"),
        ];

        foreach (var (column, rows) in new[] { (KeySheetColumnOne, left), (KeySheetColumnTwo, right) })
        {
            foreach (var (key, meaning) in rows)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                row.Children.Add(new Button
                {
                    Style = (Style)Application.Current.Resources["KeyCapButton"],
                    Content = key,
                    IsHitTestVisible = false,
                });
                row.Children.Add(new TextBlock
                {
                    Text = meaning,
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["PocketBodyText"],
                });
                column.Children.Add(row);
            }
        }
    }

    /// <summary>
    /// Bracket keys have no name in the VirtualKey enum, so their accelerators cannot be
    /// written in XAML and are registered here from their OEM scan codes.
    /// </summary>
    private void RegisterOemAccelerators()
    {
        const int oemOpenBracket = 219;
        const int oemCloseBracket = 221;
        const int oemQuestion = 191;

        Add((VirtualKey)oemOpenBracket, VirtualKeyModifiers.None, () => StepSize(-1));
        Add((VirtualKey)oemCloseBracket, VirtualKeyModifiers.None, () => StepSize(1));
        Add((VirtualKey)oemQuestion, VirtualKeyModifiers.Shift, ToggleKeySheet);

        void Add(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, args) =>
            {
                if (!ToolKeysActive())
                {
                    return;
                }

                args.Handled = true;
                action();
            };
            Root.KeyboardAccelerators.Add(accelerator);
        }
    }

    // --------------------------------------------------------------------- accelerators

    /// <summary>
    /// One guard for every accelerator on this surface. Bare letters would otherwise
    /// swallow typing in the inline text editor, and there are enough of them that
    /// repeating the check per handler is how one gets missed. Returning without setting
    /// Handled lets the key continue on to the text box.
    /// </summary>
    private bool ToolKeysActive() =>
        Content?.XamlRoot is null || FocusManager.GetFocusedElement(Content.XamlRoot) is not TextBox;

    private void ToolAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!ToolKeysActive())
        {
            return;
        }

        var tool = sender.Key switch
        {
            VirtualKey.V => AnnotationTool.Select,
            VirtualKey.A => AnnotationTool.Arrow,
            VirtualKey.L => AnnotationTool.Line,
            VirtualKey.P => AnnotationTool.Pen,
            VirtualKey.H => AnnotationTool.Highlight,
            VirtualKey.R => AnnotationTool.Box,
            VirtualKey.E => AnnotationTool.Ellipse,
            VirtualKey.T => AnnotationTool.Text,
            _ => (AnnotationTool?)null,
        };

        if (tool is null)
        {
            return;
        }

        eventArgs.Handled = true;
        SelectTool_(tool.Value);
    }

    private void InkAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!ToolKeysActive())
        {
            return;
        }

        var digit = sender.Key - VirtualKey.Number0;
        var ink = AnnotationPalette.ForKey((int)digit);
        if (ink is null)
        {
            return;
        }

        eventArgs.Handled = true;
        _ink = ink;
        ApplyToolState();
    }

    private void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        // The inline text tool commits its label on Enter, so leave the key to the
        // editor while it has focus. Everywhere else Enter saves the screenshot,
        // whether or not anything was drawn on it.
        if (!ToolKeysActive())
        {
            return;
        }

        eventArgs.Handled = true;
        _ = SaveAsync();
    }

    private void UndoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!ToolKeysActive())
        {
            return;
        }

        eventArgs.Handled = true;
        Undo();
    }

    private void RedoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!ToolKeysActive())
        {
            return;
        }

        eventArgs.Handled = true;
        Redo();
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!ToolKeysActive())
        {
            return;
        }

        eventArgs.Handled = true;
        _ = CopyAsync();
    }

    private void KeySheetAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        ToggleKeySheet();
    }

    // -------------------------------------------------------------------------- output

    private async void Save_Click(object sender, RoutedEventArgs eventArgs) => await SaveAsync();

    private async void Copy_Click(object sender, RoutedEventArgs eventArgs) => await CopyAsync();

    private async Task SaveAsync()
    {
        if (_finished)
        {
            return;
        }

        var temporary = _path + ".annotated.png";
        var marks = _history.Visible;
        await Task.Run(() => AnnotationExport.Flatten(_source, marks, temporary));
        File.Move(temporary, _path, true);
        _finished = true;
        Saved?.Invoke(this, EventArgs.Empty);
        Close();
    }

    /// <summary>
    /// Writes the marked-up image over the capture and asks for it to be re-copied,
    /// without closing the editor. The file has to be written first because the
    /// clipboard path reads it back.
    /// </summary>
    private async Task CopyAsync()
    {
        var temporary = _path + ".annotated.png";
        var marks = _history.Visible;
        await Task.Run(() => AnnotationExport.Flatten(_source, marks, temporary));
        File.Move(temporary, _path, true);
        CopyRequested?.Invoke(this, EventArgs.Empty);
        StatusToolText.Text = "COPIED · the marked-up image is on the clipboard";
    }

    private void Cancel_Click(object sender, RoutedEventArgs eventArgs) => Cancel();

    /// <summary>
    /// Escape returns an armed creation tool to Select, and closes once already in
    /// Select. The last press always keeps the original, so nothing is ever lost — it
    /// can just take two presses now. The status strip says so after the first.
    /// </summary>
    private void HandleEscape()
    {
        if (KeySheet.Visibility == Visibility.Visible)
        {
            KeySheet.Visibility = Visibility.Collapsed;
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            ClearPreview();
            if (_strokeVisual is not null)
            {
                DrawingSurface.Children.Remove(_strokeVisual);
            }

            _strokeVisual = null;
            _strokePoints = null;
            return;
        }

        if (_tool != AnnotationTool.Select)
        {
            _tool = AnnotationTool.Select;
            ApplyToolState();
            StatusToolText.Text = "SELECT · Esc again to keep the original";
            return;
        }

        Cancel();
    }

    private void Cancel()
    {
        _finished = true;
        Cancelled?.Invoke(this, EventArgs.Empty);
        Close();
    }

    // ------------------------------------------------------------------- conversions

    private static AnnPoint ToAnn(Point point) => new(point.X, point.Y);

    private static Point ToXaml(AnnPoint point) => new(point.X, point.Y);

    private static Windows.UI.Color ToWinUi(AnnColor colour) =>
        Windows.UI.Color.FromArgb(colour.A, colour.R, colour.G, colour.B);
}
