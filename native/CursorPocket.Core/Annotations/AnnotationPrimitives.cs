namespace CursorPocket.Core.Annotations;

/// <summary>
/// A point in source-image pixels. The annotation surface sizes its canvas to the
/// captured bitmap, so a canvas coordinate is an image coordinate; nudging by one
/// pixel, the native-pixel readout, and a faithful export all rest on that identity.
/// Doubles rather than floats because WinUI pointer input is double: converting once
/// at the GDI+ boundary beats converting on every pointer move.
/// </summary>
public readonly record struct AnnPoint(double X, double Y)
{
    public static AnnPoint operator +(AnnPoint a, AnnPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static AnnPoint operator -(AnnPoint a, AnnPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static AnnPoint operator *(AnnPoint a, double scale) => new(a.X * scale, a.Y * scale);

    public double Length => Math.Sqrt((X * X) + (Y * Y));
}

/// <summary>A rectangle in source-image pixels. Width and height are never negative.</summary>
public readonly record struct AnnRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public AnnPoint Center => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>Builds a normalised rectangle from any two opposite corners.</summary>
    public static AnnRect FromCorners(AnnPoint a, AnnPoint b) => new(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X),
        Math.Abs(b.Y - a.Y));
}

/// <summary>
/// A colour, kept separate from both Windows.UI.Color and System.Drawing.Color so the
/// annotation model stays in plain net8.0 alongside the rest of Core.
/// </summary>
public readonly record struct AnnColor(byte A, byte R, byte G, byte B)
{
    public static AnnColor FromHex(string hex)
    {
        var value = hex.AsSpan().TrimStart('#');
        return value.Length switch
        {
            6 => new AnnColor(
                255,
                byte.Parse(value[..2], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(value[2..4], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(value[4..6], System.Globalization.NumberStyles.HexNumber)),
            8 => new AnnColor(
                byte.Parse(value[..2], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(value[2..4], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(value[4..6], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(value[6..8], System.Globalization.NumberStyles.HexNumber)),
            _ => throw new FormatException($"'{hex}' is not a 6 or 8 digit hex colour."),
        };
    }

    public AnnColor WithAlpha(byte alpha) => new(alpha, R, G, B);
}

/// <summary>
/// Which tool is armed. Replaces the loose tool strings the first annotation surface
/// used, so an unhandled tool is a compile error rather than a silent no-op.
/// </summary>
public enum AnnotationTool
{
    Select,
    Arrow,
    Line,
    Pen,
    Highlight,
    Box,
    Ellipse,
    Text,
    Step,
    Redact,
    Focus,
    Loupe,
    Crop,
    Cut,
    Backdrop,
    ReadText,
    Eyedrop,
}

/// <summary>
/// Three sizes, not a slider. A slider is only justified where a live preview sits
/// beside it and the value is continuous; a mark's weight is neither.
/// </summary>
public enum AnnotationSizeStep
{
    Small,
    Medium,
    Large,
}

/// <summary>How a drag is being constrained while the pointer is down.</summary>
[Flags]
public enum DrawModifiers
{
    None = 0,

    /// <summary>Shift: squares, circles, and 45-degree lines.</summary>
    Constrain = 1,

    /// <summary>Alt: the press point is the centre rather than a corner.</summary>
    CenterOnPress = 2,
}
