using System.Runtime.InteropServices.WindowsRuntime;
using CursorPocket.Core.Annotations;
using CursorPocket.Core.Media;
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
    /// Smallest pointer movement, in source pixels, that is worth appending to a live
    /// freehand stroke.
    /// </summary>
    /// <remarks>
    /// Carried over from the performance pass on main. A Polyline re-tessellates its whole
    /// geometry every frame, so appending every pointer sample makes a long stroke get
    /// slower the longer it gets. Decimating at commit time (which this still does, through
    /// AnnotationGeometry.Decimate) does nothing for the drag itself.
    /// </remarks>
    private const double MinimumStrokeStep = 2.5;

    /// <summary>Corner-radius step and ceiling for a box, in source pixels.</summary>
    private const double CornerRadiusStep = 2;

    private const double MaximumCornerRadius = 24;

    private readonly CaptureRecord _record;
    private readonly string _path;
    private readonly AnnotationOrigin _origin;
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

    /// <summary>
    /// Solid first, deliberately. Pixelation and blur both derive their output from the
    /// pixels underneath, so for short text they are only partially destructive.
    /// </summary>
    private RedactStyle _redactStyle = RedactStyle.Solid;

    private FocusMode _focusMode = FocusMode.Dim;
    private FocusShape _focusShape = FocusShape.Rectangle;
    private double _boxCornerRadius;
    private double _loupeMagnification = 2;

    /// <summary>
    /// Null when Windows has no OCR recognizer installed, which disables one button
    /// rather than taking down the editor.
    /// </summary>
    private readonly OcrTextService? _ocr = OcrTextService.TryCreate();

    /// <summary>The eyedropper's last sample, offered as a seventh ink once taken.</summary>
    private AnnotationInk? _customInk;

    private Button? _customSwatch;

    /// <summary>
    /// What to go back to after the eyedropper takes its one sample. Sampling is a
    /// detour, not a mode: nobody wants to press I and then remember what they were
    /// holding.
    /// </summary>
    private AnnotationTool _toolBeforeEyedrop = AnnotationTool.Arrow;

    /// <summary>Which crop corner is being dragged, if any. Its opposite is the anchor.</summary>
    private (int Dx, int Dy)? _cropCorner;

    // The gesture in flight. Strokes append to their visual as the pointer moves;
    // everything else rebuilds its visual from the mark, so what is on screen mid-drag
    // is exactly what redo would rebuild.
    private bool _dragging;
    private Point _press;
    private Point _current;
    private List<AnnPoint>? _strokePoints;
    private Polyline? _strokeVisual;
    private Point _lastStrokePoint;

    public AnnotationWindow(CaptureRecord record, string path)
        : this(record, path, AnnotationOrigin.FreshCapture)
    {
    }

    public AnnotationWindow(CaptureRecord record, string path, AnnotationOrigin origin)
    {
        _record = record;
        _path = path;
        _origin = origin;
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
        RefreshGeometry();

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

    /// <summary>
    /// Raised with the path of a finished PNG that should become a capture of its own,
    /// leaving the original alone. Used when the geometry changed.
    /// </summary>
    public event EventHandler<string>? SavedAsNewCapture;

    /// <summary>Raised when the user throws the capture away entirely.</summary>
    public event EventHandler? Discarded;

    /// <summary>
    /// Raised when the capture should be left on screen as a pin. A fourth output
    /// alongside save, copy, and keep-original: it writes nothing and touches nothing.
    /// </summary>
    public event EventHandler? PinRequested;

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
        _toolButtons.AddRange([
            SelectTool, ArrowTool, LineTool, PenTool, HighlightTool, BoxTool, EllipseTool,
            TextTool, StepTool, RedactTool, FocusTool, CropTool, CutTool, ReadTextTool,
            EyedropTool,
        ]);

        // Degrade, never fail. OCR is a Windows language pack the user may simply not
        // have, so a missing recognizer disables one button and says why — it does not
        // offer to install anything, because there is no network in this app.
        if (_ocr is null)
        {
            ReadTextTool.IsEnabled = false;
            ToolTipService.SetToolTip(
                ReadTextTool,
                "Read text needs a Windows OCR language pack · Settings → Time & language → Language & region");
        }
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

        BuildCustomSwatch();
    }

    /// <summary>
    /// The seventh swatch: whatever the eyedropper last sampled. Until something has been
    /// sampled it shows a colour wheel, so the slot reads as "pick a colour" rather than
    /// as an empty hole.
    /// </summary>
    private void BuildCustomSwatch()
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["PocketSwatchButton"],
        };
        ToolTipService.SetToolTip(button, "Sampled colour · 7 · press I to sample");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Sampled ink");

        var stack = new StackPanel { Spacing = 1 };
        var disc = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Fill = WheelBrush(),
        };
        stack.Children.Add(disc);
        stack.Children.Add(new TextBlock
        {
            Text = "7",
            FontFamily = new FontFamily("Cascadia Mono"),
            FontSize = 10,
            IsTextScaleFactorEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["PocketMuted"],
        });

        button.Content = stack;
        button.Click += Swatch_Click;
        _customSwatch = button;
        _swatchButtons.Add(button);
        SwatchStrip.Children.Add(button);
    }

    /// <summary>
    /// The colour wheel, generated in Core and handed over as an image. WinUI ships no
    /// conic or sweep gradient brush, and a fan of wedge-shaped Paths bands visibly at
    /// this size.
    /// </summary>
    private static Brush WheelBrush()
    {
        const int size = 32;
        var pixels = ConicWheel.Render(size);
        var bitmap = new WriteableBitmap(size, size);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }

        return new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
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
            var active = ReferenceEquals(button, _customSwatch)
                ? _customInk is not null && ReferenceEquals(_ink, _customInk)
                : ReferenceEquals(button.Tag, _ink);
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

        // Each variant shows its own glyph, so the toolbar states the mode without the
        // user having to read the status strip to find out what D will do.
        RedactSolidGlyph.Visibility = Show(_redactStyle == RedactStyle.Solid);
        RedactPixelateGlyph.Visibility = Show(_redactStyle == RedactStyle.Pixelate);
        RedactBlurGlyph.Visibility = Show(_redactStyle == RedactStyle.Blur);
        FocusDimGlyph.Visibility = Show(_focusMode == FocusMode.Dim);
        FocusLoupeGlyph.Visibility = Show(_focusMode == FocusMode.Loupe);
        BackdropInnerGlyph.Visibility = Show(Geometry.BackdropIndex != 0);
        ToolTipService.SetToolTip(
            BackdropTool,
            $"Backdrop · B · {AnnotationBackdrops.At(Geometry.BackdropIndex).Name}");

        // The digit says which number is about to be placed, not a generic glyph.
        StepNumberText.Text = MarkerNumbering.Next(_history.Visible).ToString();

        SizeStepText.Text = _size switch
        {
            AnnotationSizeStep.Small => "S",
            AnnotationSizeStep.Large => "L",
            _ => "M",
        };

        StatusToolText.Text = DescribeTool();

        static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
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
        AnnotationTool.Step => $"STEP {MarkerNumbering.Next(_history.Visible)} · click to place",
        AnnotationTool.Redact => _redactStyle switch
        {
            // Solid says "nothing recoverable" outright, because the other two are not.
            RedactStyle.Solid => "REDACT · SOLID · nothing recoverable · D for pixelate",
            RedactStyle.Pixelate => "REDACT · PIXELATE · partly recoverable · D for blur",
            _ => "REDACT · BLUR · partly recoverable · D for solid",
        },
        AnnotationTool.Focus => _focusMode == FocusMode.Dim
            ? "FOCUS · DIM OUTSIDE · S for loupe"
            : $"FOCUS · LOUPE {_loupeMagnification:0.#}× {_focusShape.ToString().ToUpperInvariant()} · S to cycle",
        AnnotationTool.Eyedrop => "EYEDROPPER · click the screenshot to take its colour",
        AnnotationTool.Crop => Geometry.Crop is null
            ? "CROP · drag what to keep"
            : "CROP · drag a corner to adjust · Ctrl+Z undoes",
        AnnotationTool.Cut => "CUT · drag across the rows to remove them",
        AnnotationTool.ReadText => _ocr is null
            ? "READ TEXT · no Windows OCR language pack installed"
            : "READ TEXT · click for the whole shot, drag for a region",
        _ => _tool.ToString().ToUpperInvariant(),
    };

    private void ApplyHistoryState()
    {
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
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
                case AnnotationTool.Redact:
                    _redactStyle = _redactStyle switch
                    {
                        RedactStyle.Solid => RedactStyle.Pixelate,
                        RedactStyle.Pixelate => RedactStyle.Blur,
                        _ => RedactStyle.Solid,
                    };
                    break;
                case AnnotationTool.Focus:
                    CycleFocus();
                    break;
            }
        }
        else if (tool == AnnotationTool.Eyedrop)
        {
            // Remember what to come back to before the detour starts.
            _toolBeforeEyedrop = _tool == AnnotationTool.Eyedrop ? AnnotationTool.Arrow : _tool;
        }

        _tool = tool;
        ApplyToolState();
    }

    /// <summary>
    /// Dim, then the loupe in each of its three outlines — the same four-step cycle the
    /// reference tool uses, reached by pressing S again rather than by a submenu.
    /// </summary>
    private void CycleFocus()
    {
        (_focusMode, _focusShape) = (_focusMode, _focusShape) switch
        {
            (FocusMode.Dim, _) => (FocusMode.Loupe, FocusShape.Ellipse),
            (FocusMode.Loupe, FocusShape.Ellipse) => (FocusMode.Loupe, FocusShape.Rectangle),
            (FocusMode.Loupe, FocusShape.Rectangle) => (FocusMode.Loupe, FocusShape.Rounded),
            _ => (FocusMode.Dim, FocusShape.Rectangle),
        };
    }

    private void Swatch_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: AnnotationInk ink })
        {
            _ink = ink;
            ApplyToolState();
        }
    }

    private void Size_Click(object sender, RoutedEventArgs eventArgs) => StepSize(1, cycle: true);

    /// <summary>
    /// Steps the mark size. The toolbar button cycles, because a single button has no
    /// direction of its own; the bracket keys and the wheel do not, because a key that
    /// wrapped from largest back to smallest would feel broken.
    /// </summary>
    private void StepSize(int direction, bool cycle)
    {
        var next = AnnotationMetrics.Step(_size, direction);
        if (cycle && next == _size && direction > 0)
        {
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

        if (_tool == AnnotationTool.Step)
        {
            // A marker is placed, not dragged: one click, one number.
            Commit(new MarkerMark
            {
                Id = _history.AllocateId(),
                Colour = _ink.Colour,
                StrokeWidth = CurrentStrokeWidth,
                Center = ToAnn(_press),
                Number = MarkerNumbering.Next(_history.Visible),
                Radius = MarkerNumbering.RadiusFor(_sourceWidth, _sourceHeight, _size),
            });
            return;
        }

        if (_tool == AnnotationTool.Eyedrop)
        {
            SampleColour(_press);
            return;
        }

        if (_tool == AnnotationTool.Select)
        {
            // Nothing to select yet: marks are not yet individually addressable. The
            // status strip says so rather than the click doing nothing unexplained.
            return;
        }

        // Grabbing a corner adjusts the existing crop; pressing anywhere else replaces it.
        _cropCorner = _tool == AnnotationTool.Crop ? CropCornerAt(_press) : null;

        DrawingSurface.CapturePointer(eventArgs.Pointer);
        _dragging = true;

        if (_tool is AnnotationTool.Pen or AnnotationTool.Highlight)
        {
            _strokePoints = [ToAnn(_press)];
            _lastStrokePoint = _press;
            var highlighting = _tool == AnnotationTool.Highlight;
            _strokeVisual = new Polyline
            {
                // Opaque brush plus element opacity, matching the committed visual: a
                // translucent brush would make the stroke darken itself as it crossed
                // over, and then jump when the commit replaced it with the correct one.
                Stroke = new SolidColorBrush(ToWinUi(StrokeColour().WithAlpha(255))),
                Opacity = highlighting ? AnnotationPalette.HighlightAlpha / 255d : 1,
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
            // every pointer move would be quadratic over a long stroke. Samples closer
            // than one step are dropped for the same reason — the Polyline re-tessellates
            // everything it holds on every frame, so a long stroke would otherwise get
            // slower the longer it got. The commit-time smoothing pass reads the same
            // point list, so the saved shape is unaffected.
            if (SquaredDistance(_current, _lastStrokePoint) >= MinimumStrokeStep * MinimumStrokeStep)
            {
                _lastStrokePoint = _current;
                _strokePoints.Add(ToAnn(_current));
                _strokeVisual.Points.Add(_current);
            }

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

        if (_tool == AnnotationTool.Crop)
        {
            var frame = PendingCrop();
            _cropCorner = null;
            if (frame.Width >= 8 && frame.Height >= 8)
            {
                ApplyGeometry(Geometry with { Crop = frame });
                RefreshGeometry();
                ApplyHistoryState();
            }

            ApplyToolState();
            UpdateReadout();
            return;
        }

        if (_tool == AnnotationTool.Cut)
        {
            var band = PendingCut();
            if (band.Length >= 4)
            {
                ApplyGeometry(Geometry with { Cuts = [.. Geometry.Cuts, band] });
                RefreshGeometry();
                ApplyHistoryState();
            }

            ApplyToolState();
            UpdateReadout();
            return;
        }

        if (_tool == AnnotationTool.ReadText)
        {
            // A drag reads that rectangle; a click, which leaves a rect of nothing, reads
            // the whole shot. Reading commits no mark, so the tool stays armed.
            var region = CurrentRect(CurrentModifiers());
            _ = ReadTextAsync(region.Width > 4 && region.Height > 4 ? region : null);
            UpdateReadout();
            return;
        }

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
    /// The wheel changes the active tool's size. Holding Alt changes the secondary knob
    /// instead — a box's corner radius, or the loupe's magnification.
    /// </summary>
    private void DrawingSurface_PointerWheelChanged(object sender, PointerRoutedEventArgs eventArgs)
    {
        var delta = eventArgs.GetCurrentPoint(DrawingSurface).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        eventArgs.Handled = true;
        var direction = Math.Sign(delta);

        if (!IsDown(VirtualKey.Menu))
        {
            StepSize(direction, cycle: false);
            return;
        }

        switch (_tool)
        {
            case AnnotationTool.Box:
                _boxCornerRadius = Math.Clamp(_boxCornerRadius + (direction * CornerRadiusStep), 0, MaximumCornerRadius);
                StatusSizeText.Text = $"radius {_boxCornerRadius:0} px";
                break;
            case AnnotationTool.Focus when _focusMode == FocusMode.Loupe:
                _loupeMagnification = Math.Clamp(_loupeMagnification + (direction * 0.25), 1.25, 6);
                ApplyToolState();
                break;
            default:
                StepSize(direction, cycle: false);
                break;
        }
    }

    /// <summary>
    /// Samples the screenshot's own pixels and offers the result as the seventh ink.
    /// Reads the decoded source, never the screen: sampling the screen would pick up
    /// CursorPocket's own toolbar sitting over the image.
    /// </summary>
    private void SampleColour(Point point)
    {
        var x = Math.Clamp((int)Math.Round(point.X), 0, _sourceWidth - 1);
        var y = Math.Clamp((int)Math.Round(point.Y), 0, _sourceHeight - 1);
        var sampled = _source.GetPixel(x, y);
        var colour = new AnnColor(255, sampled.R, sampled.G, sampled.B);

        _customInk = new AnnotationInk("Sampled", $"#{sampled.R:X2}{sampled.G:X2}{sampled.B:X2}");
        _ink = _customInk;

        if (_customSwatch?.Content is StackPanel { Children: [Microsoft.UI.Xaml.Shapes.Ellipse disc, _] })
        {
            // The wheel was the "nothing sampled yet" state; a flat disc of the actual
            // colour is more informative than a gradient once there is one to show.
            disc.Fill = new SolidColorBrush(ToWinUi(colour));
        }

        // Sampling is a detour, not a mode.
        _tool = _toolBeforeEyedrop;
        ApplyToolState();
        StatusToolText.Text = $"SAMPLED #{sampled.R:X2}{sampled.G:X2}{sampled.B:X2} · now inking with it";
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
                    : new BoxMark
                    {
                        Id = id,
                        Colour = colour,
                        StrokeWidth = width,
                        Rect = rect,
                        Filled = _boxFilled,
                        CornerRadius = _boxCornerRadius,
                    };
            }

            case AnnotationTool.Redact:
            {
                var rect = CurrentRect(modifiers);
                // A redaction narrower than this cannot hold a block and is almost
                // certainly a stray click rather than an intent to obscure something.
                return rect.Width < 4 || rect.Height < 4
                    ? null
                    : new RedactMark
                    {
                        Id = id,
                        Colour = colour,
                        StrokeWidth = width,
                        Rect = rect,
                        Style = _redactStyle,
                    };
            }

            case AnnotationTool.Focus:
            {
                var rect = CurrentRect(modifiers);
                return rect.Width < 8 || rect.Height < 8
                    ? null
                    : new FocusMark
                    {
                        Id = id,
                        Colour = colour,
                        StrokeWidth = width,
                        Rect = rect,
                        Mode = _focusMode,
                        Shape = _focusShape,
                        Magnification = _loupeMagnification,
                    };
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

        // One placement, then back to Select — and the new mark is deliberately not
        // selected, because fresh handles on a just-drawn arrow are noise. The cost is
        // that three arrows in a row means pressing A three times; the benefit is that a
        // stray drag can never add a mark you did not mean to place.
        _tool = AnnotationTool.Select;
        ApplyToolState();
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

        if (_tool == AnnotationTool.Crop)
        {
            var frame = PendingCrop();
            var outline = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = frame.Width,
                Height = frame.Height,
                Stroke = (Brush)Application.Current.Resources["PocketGreen"],
                StrokeThickness = Math.Max(1, CurrentStrokeWidth / 3),
            };
            Canvas.SetLeft(outline, frame.X);
            Canvas.SetTop(outline, frame.Y);
            _preview = outline;
            DrawingSurface.Children.Add(_preview);
            return;
        }

        if (_tool == AnnotationTool.Cut)
        {
            var band = PendingCut();
            var strip = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = _sourceWidth,
                Height = band.Length,
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(AnnotationExport.DimAlpha, 0, 0, 0)),
                Stroke = (Brush)Application.Current.Resources["PocketBlue"],
                StrokeThickness = Math.Max(1, CurrentStrokeWidth / 3),
                StrokeDashArray = [4, 3],
            };
            Canvas.SetLeft(strip, 0);
            Canvas.SetTop(strip, band.Offset);
            _preview = strip;
            DrawingSurface.Children.Add(_preview);
            return;
        }

        if (_tool == AnnotationTool.ReadText)
        {
            // A dashed marquee rather than a mark: this region is being read, not drawn,
            // and nothing will be committed when the pointer comes up.
            var region = CurrentRect(CurrentModifiers());
            var marquee = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = region.Width,
                Height = region.Height,
                Stroke = (Brush)Application.Current.Resources["PocketBlue"],
                StrokeThickness = Math.Max(1, CurrentStrokeWidth / 3),
                StrokeDashArray = [4, 3],
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(24, 127, 187, 255)),
            };
            Canvas.SetLeft(marquee, region.X);
            Canvas.SetTop(marquee, region.Y);
            _preview = marquee;
            DrawingSurface.Children.Add(_preview);
            return;
        }

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
                // A highlighter strokes opaque and composites once, through the element's
                // own opacity. Stroking a translucent brush directly makes a single
                // stroke darken itself everywhere it crosses over, which a highlighter
                // never does on paper. The exporter does the same thing with a layer
                // bitmap, so the two agree.
                var opaque = stroke.Highlight
                    ? new SolidColorBrush(ToWinUi(stroke.Colour.WithAlpha(255)))
                    : brush;
                var polyline = new Polyline
                {
                    Stroke = opaque,
                    Opacity = stroke.Highlight ? stroke.Colour.A / 255d : 1,
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
                    RadiusX = box.CornerRadius,
                    RadiusY = box.CornerRadius,
                    Stroke = brush,
                    StrokeThickness = box.StrokeWidth,
                    // Filled means filled in the file too. The old surface previewed a
                    // faint fill on every box and exported none of it.
                    Fill = box.Filled
                        ? new SolidColorBrush(ToWinUi(box.Colour.WithAlpha(AnnotationExport.FillAlpha)))
                        : null,
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

            case MarkerMark marker:
            {
                // A disc with the number inside it. The digit's colour comes from Core so
                // it matches the exporter's choice: Citron needs dark digits and Violet
                // needs light ones, and two renderers guessing separately disagree.
                var host = new Grid { Width = marker.Radius * 2, Height = marker.Radius * 2 };
                host.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = brush });
                host.Children.Add(new TextBlock
                {
                    Text = marker.Number.ToString(),
                    FontSize = marker.Radius * (marker.Number > 9 ? 1.05 : 1.3),
                    FontFamily = new FontFamily(AnnotationExport.TextFamily),
                    Foreground = new SolidColorBrush(ToWinUi(AnnotationPalette.OnInk(marker.Colour))),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsTextScaleFactorEnabled = false,
                });
                Canvas.SetLeft(host, marker.Center.X - marker.Radius);
                Canvas.SetTop(host, marker.Center.Y - marker.Radius);
                return host;
            }

            case RedactMark redact:
                return PatchImage(AnnotationPatches.Redact(_source, redact), redact.Rect, null, FocusShape.Rectangle);

            case FocusMark { Mode: FocusMode.Loupe } loupe:
                return PatchImage(
                    AnnotationPatches.Loupe(_source, loupe),
                    loupe.Rect,
                    new SolidColorBrush(ToWinUi(loupe.Colour)),
                    loupe.Shape,
                    loupe.StrokeWidth);

            case FocusMark dim:
                return DimOutside(dim);

            case TextMark text:
                return TextVisual(text, brush);

            default:
                throw new NotSupportedException($"No visual for {mark.GetType().Name}.");
        }
    }

    /// <summary>
    /// Shows a pixel patch on the canvas, optionally clipped to a shape and ringed. The
    /// patch itself comes from <see cref="AnnotationPatches"/>, which the exporter also
    /// calls, so the pixels on screen are the pixels in the file.
    /// </summary>
    private UIElement PatchImage(
        AnnotationPatches.Patch patch,
        AnnRect rect,
        Brush? ring,
        FocusShape shape,
        double ringWidth = 0)
    {
        var host = new Grid { Width = rect.Width, Height = rect.Height };

        if (!patch.IsEmpty)
        {
            var bitmap = new WriteableBitmap(patch.Width, patch.Height);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(patch.Pixels, 0, patch.Pixels.Length);
            }

            // The patch is painted as a shape's fill rather than shown as an Image with a
            // clip, because UIElement.Clip only accepts a RectangleGeometry — it cannot
            // cut an ellipse. Filling the shape handles all three outlines the same way.
            var canvasShape = ShapeFor(rect, shape);
            canvasShape.Fill = new ImageBrush { ImageSource = bitmap, Stretch = Stretch.Fill };
            host.Children.Add(canvasShape);
        }

        if (ring is not null)
        {
            var outline = ShapeFor(rect, shape);
            outline.Stroke = ring;
            outline.StrokeThickness = ringWidth;
            host.Children.Add(outline);
        }

        Canvas.SetLeft(host, rect.X);
        Canvas.SetTop(host, rect.Y);
        return host;
    }

    /// <summary>
    /// An unpainted shape the size of a focus region. The rounded radius comes from the
    /// exporter's own helper, so a rounded region on screen is the same curve as the one
    /// in the file.
    /// </summary>
    private static Microsoft.UI.Xaml.Shapes.Shape ShapeFor(AnnRect rect, FocusShape shape)
    {
        if (shape == FocusShape.Ellipse)
        {
            return new Microsoft.UI.Xaml.Shapes.Ellipse { Width = rect.Width, Height = rect.Height };
        }

        var radius = shape == FocusShape.Rounded
            ? AnnotationExport.CornerRadiusFor(rect.Width, rect.Height)
            : 0;
        return new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            RadiusX = radius,
            RadiusY = radius,
        };
    }

    /// <summary>
    /// Darkens everything outside a region, by filling the whole image with one geometry
    /// group whose even-odd rule punches the region out. This mirrors the exporter's
    /// GraphicsPath with FillMode.Alternate exactly; four surrounding bands would leave
    /// the corners of an elliptical or rounded region undimmed.
    /// </summary>
    private UIElement DimOutside(FocusMark focus)
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, _sourceWidth, _sourceHeight),
        });
        group.Children.Add(HoleGeometry(focus));

        return new ShapePath
        {
            Data = group,
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(AnnotationExport.DimAlpha, 0, 0, 0)),
            IsHitTestVisible = false,
        };
    }

    private static Geometry HoleGeometry(FocusMark focus)
    {
        var rect = new Windows.Foundation.Rect(focus.Rect.X, focus.Rect.Y, focus.Rect.Width, focus.Rect.Height);
        switch (focus.Shape)
        {
            case FocusShape.Ellipse:
                return new EllipseGeometry
                {
                    Center = new Point(focus.Rect.Center.X, focus.Rect.Center.Y),
                    RadiusX = focus.Rect.Width / 2,
                    RadiusY = focus.Rect.Height / 2,
                };

            case FocusShape.Rounded:
                return RoundedGeometry(rect, AnnotationExport.CornerRadiusFor(rect.Width, rect.Height));

            default:
                return new RectangleGeometry { Rect = rect };
        }
    }

    /// <summary>
    /// A rounded rectangle as a Geometry. WinUI has no rounded RectangleGeometry, and the
    /// abbreviated path syntax cannot be used here either — it only parses through
    /// Path.Data's type converter — so the figure is assembled by hand.
    /// </summary>
    private static PathGeometry RoundedGeometry(Windows.Foundation.Rect rect, double radius)
    {
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        var figure = new PathFigure
        {
            StartPoint = new Point(rect.Left + r, rect.Top),
            IsClosed = true,
            IsFilled = true,
        };

        var size = new Windows.Foundation.Size(r, r);
        figure.Segments.Add(new LineSegment { Point = new Point(rect.Right - r, rect.Top) });
        figure.Segments.Add(Arc(new Point(rect.Right, rect.Top + r), size));
        figure.Segments.Add(new LineSegment { Point = new Point(rect.Right, rect.Bottom - r) });
        figure.Segments.Add(Arc(new Point(rect.Right - r, rect.Bottom), size));
        figure.Segments.Add(new LineSegment { Point = new Point(rect.Left + r, rect.Bottom) });
        figure.Segments.Add(Arc(new Point(rect.Left, rect.Bottom - r), size));
        figure.Segments.Add(new LineSegment { Point = new Point(rect.Left, rect.Top + r) });
        figure.Segments.Add(Arc(new Point(rect.Left + r, rect.Top), size));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;

        static ArcSegment Arc(Point point, Windows.Foundation.Size size) => new()
        {
            Point = point,
            Size = size,
            SweepDirection = SweepDirection.Clockwise,
        };
    }

    /// <summary>
    /// Annotation text, with or without its readability pill. The pill is sized from the
    /// exporter's own measurer rather than from WinUI's, so the pill on screen is the
    /// same rectangle as the pill in the file — two text stacks measure differently, and
    /// letting each size its own pill is how they drift.
    /// </summary>
    private static UIElement TextVisual(TextMark text, Brush brush)
    {
        var measured = AnnotationExport.MeasureText(text.Text, text.FontSize);
        var pad = text.FontSize * AnnotationExport.PillPaddingFactor;

        var label = new TextBlock
        {
            Text = text.Text,
            FontSize = text.FontSize,
            FontFamily = new FontFamily(AnnotationExport.TextFamily),
            IsTextScaleFactorEnabled = false,
        };

        if (!text.Pill)
        {
            label.Foreground = brush;
            label.Margin = new Thickness(pad, pad / 2, 0, 0);
            var bare = new Grid();
            bare.Children.Add(label);
            Canvas.SetLeft(bare, text.Anchor.X);
            Canvas.SetTop(bare, text.Anchor.Y);
            return bare;
        }

        label.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 11, 16, 15));
        var pill = new Border
        {
            Width = measured.Width + (pad * 2),
            Height = measured.Height + pad,
            Padding = new Thickness(pad, pad / 2, pad, pad / 2),
            CornerRadius = new CornerRadius(
                AnnotationExport.CornerRadiusFor(measured.Width + (pad * 2), measured.Height + pad)),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(240, 242, 247, 244)),
            Child = label,
        };
        Canvas.SetLeft(pill, text.Anchor.X);
        Canvas.SetTop(pill, text.Anchor.Y);
        return pill;
    }

    // ----------------------------------------------------------------- image geometry

    /// <summary>The crop, cuts, and backdrop currently in effect.</summary>
    private DocumentGeometry Geometry => _history.Geometry;

    /// <summary>
    /// The transform from screenshot pixels to exported pixels. Rebuilt on demand rather
    /// than cached, because it is derived entirely from the history and a stale copy would
    /// be worse than a cheap rebuild.
    /// </summary>
    private DocumentTransform BuildTransform()
    {
        var geometry = Geometry;
        var crop = geometry.Crop;
        var content = crop ?? new AnnRect(0, 0, _sourceWidth, _sourceHeight);
        // Backdrop padding scales off the cropped image, not the original: a preset should
        // look the same whether the shot was cropped first or not.
        var backdrop = AnnotationBackdrops.Resolve(
            geometry.BackdropIndex,
            (int)Math.Round(content.Width),
            (int)Math.Round(content.Height));
        return DocumentTransform.Build(_sourceWidth, _sourceHeight, crop, geometry.Cuts, backdrop);
    }

    private void ApplyGeometry(DocumentGeometry geometry) =>
        _history.Add(new GeometryStep(geometry));

    /// <summary>
    /// The crop the current drag describes. Dragging a corner moves that corner and holds
    /// its opposite; dragging anywhere else draws a fresh rectangle.
    /// </summary>
    private AnnRect PendingCrop()
    {
        if (_cropCorner is { } corner && Geometry.Crop is { } existing)
        {
            var anchorX = corner.Dx < 0 ? existing.Right : existing.X;
            var anchorY = corner.Dy < 0 ? existing.Bottom : existing.Y;
            return AnnotationGeometry.ClampToImage(
                AnnRect.FromCorners(new AnnPoint(anchorX, anchorY), ToAnn(_current)),
                _sourceWidth,
                _sourceHeight);
        }

        return CurrentRect(CurrentModifiers());
    }

    /// <summary>The strip the current drag would remove, as whole source rows.</summary>
    private CutBand PendingCut()
    {
        var top = Math.Clamp(Math.Min(_press.Y, _current.Y), 0, _sourceHeight);
        var bottom = Math.Clamp(Math.Max(_press.Y, _current.Y), 0, _sourceHeight);
        return new CutBand(top, Math.Max(0, bottom - top));
    }

    private void Backdrop_Click(object sender, RoutedEventArgs eventArgs) => CycleBackdrop();

    private void BackdropAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!ToolKeysActive())
        {
            return;
        }

        eventArgs.Handled = true;
        CycleBackdrop();
    }

    private void CycleBackdrop()
    {
        var geometry = Geometry;
        ApplyGeometry(geometry with { BackdropIndex = AnnotationBackdrops.Next(geometry.BackdropIndex) });
        RefreshGeometry();
    }

    /// <summary>
    /// Redraws everything that depends on the geometry: the backdrop frame, the crop mask
    /// and its handles, the cut seams, and the size readout.
    /// </summary>
    private void RefreshGeometry()
    {
        var transform = BuildTransform();
        var geometry = Geometry;

        // The backdrop is previewed by framing the stage, so the preview is the shape.
        var backdrop = transform.Backdrop;
        if (backdrop.IsEnabled)
        {
            BackdropFrame.Padding = new Thickness(backdrop.Padding);
            BackdropFrame.Background = new SolidColorBrush(ToWinUi(backdrop.Fill));
        }
        else
        {
            BackdropFrame.Padding = new Thickness(0);
            BackdropFrame.Background = null;
        }

        GeometryOverlay.Children.Clear();
        var dim = new SolidColorBrush(Windows.UI.Color.FromArgb(AnnotationExport.DimAlpha, 0, 0, 0));
        var green = (Brush)Application.Current.Resources["PocketGreen"];

        if (geometry.Crop is { } crop)
        {
            // Everything outside the crop is dimmed rather than hidden, so the user can
            // still see what they are giving up and drag a corner back out to reclaim it.
            AddBand(0, 0, _sourceWidth, crop.Y);
            AddBand(0, crop.Bottom, _sourceWidth, _sourceHeight - crop.Bottom);
            AddBand(0, crop.Y, crop.X, crop.Height);
            AddBand(crop.Right, crop.Y, _sourceWidth - crop.Right, crop.Height);

            // Corner brackets rather than filled squares: the same green as the active
            // tool, told apart by form, which is the rule the app already uses for
            // capture kinds.
            var arm = Math.Max(12, Math.Min(crop.Width, crop.Height) * 0.08);
            var weight = Math.Max(2, arm / 6);
            AddBracket(crop.X, crop.Y, arm, weight, 1, 1);
            AddBracket(crop.Right, crop.Y, arm, weight, -1, 1);
            AddBracket(crop.X, crop.Bottom, arm, weight, 1, -1);
            AddBracket(crop.Right, crop.Bottom, arm, weight, -1, -1);
        }

        foreach (var band in geometry.Cuts)
        {
            var strip = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = _sourceWidth,
                Height = band.Length,
                Fill = dim,
                Stroke = (Brush)Application.Current.Resources["PocketBlue"],
                StrokeThickness = Math.Max(1, _sourceHeight * 0.002),
                StrokeDashArray = [4, 3],
            };
            Canvas.SetLeft(strip, 0);
            Canvas.SetTop(strip, band.Offset);
            GeometryOverlay.Children.Add(strip);
        }

        UpdateOutputReadout(transform);
        return;

        void AddBand(double x, double y, double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var band = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = width, Height = height, Fill = dim };
            Canvas.SetLeft(band, x);
            Canvas.SetTop(band, y);
            GeometryOverlay.Children.Add(band);
        }

        void AddBracket(double x, double y, double arm, double weight, int dx, int dy)
        {
            Add(x - (dx < 0 ? arm : 0), y - (dy < 0 ? weight : 0), arm, weight);
            Add(x - (dx < 0 ? weight : 0), y - (dy < 0 ? arm : 0), weight, arm);

            void Add(double bx, double by, double bw, double bh)
            {
                var bar = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = bw, Height = bh, Fill = green };
                Canvas.SetLeft(bar, bx);
                Canvas.SetTop(bar, by);
                GeometryOverlay.Children.Add(bar);
            }
        }
    }

    /// <summary>
    /// States the size the export will actually be, which is the whole reason the readout
    /// claims native pixels.
    /// </summary>
    private void UpdateOutputReadout(DocumentTransform transform)
    {
        StatusOutputText.Text = transform.IsIdentity
            ? $"{_sourceWidth} × {_sourceHeight}"
            : $"→ {transform.OutputWidth} × {transform.OutputHeight}";
    }

    /// <summary>
    /// The corner of the crop nearest a point, if the point is close enough to grab it.
    /// Dragging a corner adjusts the crop instead of replacing it.
    /// </summary>
    private (int Dx, int Dy)? CropCornerAt(Point point)
    {
        if (Geometry.Crop is not { } crop)
        {
            return null;
        }

        var tolerance = Math.Max(12, Math.Min(_sourceWidth, _sourceHeight) * 0.03);
        foreach (var (x, y, dx, dy) in new[]
                 {
                     (crop.X, crop.Y, -1, -1),
                     (crop.Right, crop.Y, 1, -1),
                     (crop.X, crop.Bottom, -1, 1),
                     (crop.Right, crop.Bottom, 1, 1),
                 })
        {
            if (Math.Abs(point.X - x) <= tolerance && Math.Abs(point.Y - y) <= tolerance)
            {
                return (dx, dy);
            }
        }

        return null;
    }

    // ---------------------------------------------------------------------------- OCR

    /// <summary>
    /// Reads text out of the screenshot. A click reads the whole image, a drag reads that
    /// rectangle — one key, both behaviours, discoverable by trying.
    /// </summary>
    private async Task ReadTextAsync(AnnRect? region)
    {
        if (_ocr is null)
        {
            return;
        }

        var rect = region is { } area && area.Width > 4 && area.Height > 4
            ? AnnotationPatches.Snap(area, _sourceWidth, _sourceHeight)
            : new System.Drawing.Rectangle(0, 0, _sourceWidth, _sourceHeight);

        StatusToolText.Text = "READING…";
        OcrReading? reading;
        try
        {
            reading = await _ocr.ReadAsync(_source, rect);
        }
        catch (Exception)
        {
            // Degrade, never fail: a recognition error costs the reading, never the shot.
            StatusToolText.Text = "READ TEXT · that region could not be read";
            return;
        }

        if (reading is null)
        {
            StatusToolText.Text = "READ TEXT · that region is too small or too large to read";
            return;
        }

        ShowOcr(reading);
    }

    private void ShowOcr(OcrReading reading)
    {
        OcrText.Text = reading.Text;
        OcrSummaryText.Text = reading.WordCount == 0
            ? $"no text found · {reading.Language}"
            : $"{reading.WordCount} words · {reading.Language}";
        OcrPanel.Visibility = Visibility.Visible;

        // Faint boxes over each word, so it is obvious which part of the image the text
        // came from. Informational blue is reserved for text and link captures, and this
        // is a text capture.
        OcrOverlay.Children.Clear();
        var stroke = (Brush)Application.Current.Resources["PocketBlue"];
        foreach (var word in reading.Words)
        {
            var box = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = word.Bounds.Width,
                Height = word.Bounds.Height,
                Stroke = stroke,
                StrokeThickness = 1,
                Opacity = 0.4,
            };
            Canvas.SetLeft(box, word.Bounds.X);
            Canvas.SetTop(box, word.Bounds.Y);
            OcrOverlay.Children.Add(box);
        }

        StatusToolText.Text = reading.WordCount == 0
            ? "READ TEXT · nothing recognised here"
            : $"READ TEXT · {reading.WordCount} words · Ctrl+Shift+C copies";
    }

    private void CloseOcr_Click(object sender, RoutedEventArgs eventArgs) => HideOcr();

    private void HideOcr()
    {
        OcrPanel.Visibility = Visibility.Collapsed;
        OcrOverlay.Children.Clear();
        OcrText.Text = string.Empty;
    }

    private void CopyOcrText_Click(object sender, RoutedEventArgs eventArgs) => CopyOcrText();

    /// <summary>
    /// Puts the recognised text on the clipboard, only ever when asked. A screenshot is
    /// on the clipboard from the moment it is taken; silently replacing that with text
    /// would break a promise the app makes everywhere else.
    /// </summary>
    private void CopyOcrText()
    {
        if (string.IsNullOrEmpty(OcrText.Text))
        {
            return;
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(OcrText.Text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        StatusToolText.Text = "TEXT COPIED · the screenshot is no longer on the clipboard";
    }

    private async void SaveOcrText_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(OcrText.Text))
        {
            return;
        }

        try
        {
            // A normal text capture, so it gets a receipt and a Library row like any
            // other. OCR is the same capture kind reached a different way.
            await App.Services.CaptureStore.SaveTextAsync(OcrText.Text);
            StatusToolText.Text = "SAVED · the recognised text is now its own capture";
        }
        catch (Exception error)
        {
            StatusToolText.Text = $"NOT SAVED · {error.Message}";
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
        switch (_history.Undo())
        {
            case null:
                return;

            case MarkStep mark:
                if (_visuals.Remove(mark.Mark.Id, out var visual))
                {
                    DrawingSurface.Children.Remove(visual);
                }

                break;

            case GeometryStep:
                // Nothing to unwind by hand: the geometry is read back off the history, so
                // stepping the index back is the undo.
                RefreshGeometry();
                break;
        }

        ApplyHistoryState();
        ApplyToolState();
    }

    private void Redo()
    {
        switch (_history.Redo())
        {
            case null:
                return;

            case MarkStep mark:
                // Rebuilt from the mark, which is only possible because a mark is data
                // rather than a live element. The old surface could not offer redo for
                // exactly that reason.
                AddVisual(mark.Mark);
                break;

            case GeometryStep:
                RefreshGeometry();
                break;
        }

        ApplyHistoryState();
        ApplyToolState();
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
            ("Wheel", "smaller or larger marks"),
            ("Alt+Wheel", "box corner radius · loupe zoom"),
            ("[  ]", "smaller or larger marks"),
        ];
        (string Key, string Meaning)[] right =
        [
            ("1 – 6", "ink colour · 7 is the sampled one"),
            ("Ctrl+Z", "undo · Ctrl+Y redoes"),
            ("Ctrl+C", "copy without saving"),
            ("Enter", "save · Esc keeps the original"),
            ("F1", "hide this"),
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

        Add((VirtualKey)oemOpenBracket, VirtualKeyModifiers.None, () => StepSize(-1, cycle: false));
        Add((VirtualKey)oemCloseBracket, VirtualKeyModifiers.None, () => StepSize(1, cycle: false));
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
            VirtualKey.N => AnnotationTool.Step,
            VirtualKey.D => AnnotationTool.Redact,
            VirtualKey.S => AnnotationTool.Focus,
            VirtualKey.I => AnnotationTool.Eyedrop,
            VirtualKey.O => AnnotationTool.ReadText,
            VirtualKey.C => AnnotationTool.Crop,
            VirtualKey.X => AnnotationTool.Cut,
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

        var digit = (int)(sender.Key - VirtualKey.Number0);

        // 7 is the eyedropper's slot. Until something has been sampled there is no ink
        // there, so the key arms the eyedropper instead of selecting nothing.
        if (digit == AnnotationPalette.Inks.Count + 1)
        {
            eventArgs.Handled = true;
            if (_customInk is null)
            {
                SelectTool_(AnnotationTool.Eyedrop);
            }
            else
            {
                _ink = _customInk;
                ApplyToolState();
            }

            return;
        }

        var ink = AnnotationPalette.ForKey(digit);
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

    /// <summary>
    /// Copies the recognised text. Deliberately its own key rather than sharing Ctrl+C:
    /// the screenshot is on the clipboard from the moment it is taken, and taking that
    /// away without being asked would break a promise the app makes everywhere else.
    /// </summary>
    private void CopyTextAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (OcrPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        eventArgs.Handled = true;
        CopyOcrText();
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

        var marks = _history.Visible;
        var transform = BuildTransform();
        var mode = SaveTarget.For(
            marksChanged: _history.HasVisibleMarks,
            geometryChanged: !Geometry.IsUntouched,
            origin: _origin);

        var temporary = _path + ".annotated.png";
        await Task.Run(() => AnnotationExport.Flatten(_source, marks, transform, temporary));

        if (mode == AnnotationSaveMode.Overwrite)
        {
            File.Move(temporary, _path, true);
            _finished = true;
            Saved?.Invoke(this, EventArgs.Empty);
            Close();
            return;
        }

        // A geometry change deletes pixels, and a save overwrites rather than deleting, so
        // there would be no Recycle Bin copy to go back to. The original capture is left
        // exactly as it was and the result becomes a capture of its own, which also means
        // its width, height, and preview text are right without repairing the index.
        _finished = true;
        SavedAsNewCapture?.Invoke(this, temporary);
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
        var transform = BuildTransform();
        await Task.Run(() => AnnotationExport.Flatten(_source, marks, transform, temporary));
        File.Move(temporary, _path, true);
        CopyRequested?.Invoke(this, EventArgs.Empty);
        StatusToolText.Text = "COPIED · the marked-up image is on the clipboard";
    }

    private void Pin_Click(object sender, RoutedEventArgs eventArgs) => Pin();

    private void PinAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!ToolKeysActive())
        {
            return;
        }

        eventArgs.Handled = true;
        Pin();
    }

    /// <summary>
    /// Saves, then leaves the result on screen. Saving first is what makes the pin a
    /// reference to a real capture rather than to a temporary file that will be gone the
    /// next time the pin's Mark up button is pressed.
    /// </summary>
    private void Pin()
    {
        PinRequested?.Invoke(this, EventArgs.Empty);
        _ = SaveAsync();
    }

    private void Cancel_Click(object sender, RoutedEventArgs eventArgs) => Cancel();

    /// <summary>
    /// Throws the whole capture away. The file was written before this window opened, so
    /// this is the only way to undo having taken the shot — and it goes to the Recycle
    /// Bin, never a hard delete.
    /// </summary>
    private void Discard_Click(object sender, RoutedEventArgs eventArgs)
    {
        _finished = true;
        Discarded?.Invoke(this, EventArgs.Empty);
        Close();
    }

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

        // Anything overlaid closes before the editor does, so Escape never skips past a
        // panel to discard the whole session.
        if (OcrPanel.Visibility == Visibility.Visible)
        {
            HideOcr();
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

    private static double SquaredDistance(Point first, Point second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return (dx * dx) + (dy * dy);
    }

    private static AnnPoint ToAnn(Point point) => new(point.X, point.Y);

    private static Point ToXaml(AnnPoint point) => new(point.X, point.Y);

    private static Windows.UI.Color ToWinUi(AnnColor colour) =>
        Windows.UI.Color.FromArgb(colour.A, colour.R, colour.G, colour.B);
}
