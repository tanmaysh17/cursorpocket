namespace CursorPocket.Core.Annotations;

[Flags]
public enum AnnotationToolProperties
{
    None = 0,
    Ink = 1,
    Size = 2,
    Variant = 4,
    Results = 8,
}

public sealed record AnnotationToolDescriptor(
    AnnotationTool Tool,
    string Label,
    string Key,
    string Glyph,
    AnnotationToolProperties Properties = AnnotationToolProperties.None);

/// <summary>
/// The one public inventory for the annotation workspace. The list intentionally has
/// sixteen visible entries: loupe is a Focus variant, not a tool hidden in another menu.
/// </summary>
public static class AnnotationToolCatalog
{
    public static IReadOnlyList<AnnotationToolDescriptor> All { get; } =
    [
        new(AnnotationTool.Select, "Select", "V", "\uE8B0"),
        new(AnnotationTool.Arrow, "Arrow", "A", "\uE72A", AnnotationToolProperties.Ink | AnnotationToolProperties.Size),
        new(AnnotationTool.Line, "Line", "L", "\uE738", AnnotationToolProperties.Ink | AnnotationToolProperties.Size),
        new(AnnotationTool.Pen, "Pen", "P", "\uED63", AnnotationToolProperties.Ink | AnnotationToolProperties.Size),
        new(AnnotationTool.Highlight, "Highlight", "H", "\uE7E6", AnnotationToolProperties.Ink | AnnotationToolProperties.Size),
        new(AnnotationTool.Box, "Box", "R", "\uE739", AnnotationToolProperties.Ink | AnnotationToolProperties.Size | AnnotationToolProperties.Variant),
        new(AnnotationTool.Ellipse, "Ellipse", "E", "\uEA3A", AnnotationToolProperties.Ink | AnnotationToolProperties.Size | AnnotationToolProperties.Variant),
        new(AnnotationTool.Text, "Text", "T", "\uE8D2", AnnotationToolProperties.Ink | AnnotationToolProperties.Size),
        new(AnnotationTool.Step, "Step", "N", "\uE9F9", AnnotationToolProperties.Ink),
        new(AnnotationTool.Redact, "Redact", "D", "\uE8C3", AnnotationToolProperties.Variant),
        new(AnnotationTool.Focus, "Focus", "S", "\uE71E", AnnotationToolProperties.Variant),
        new(AnnotationTool.Eyedrop, "Eyedropper", "I", "\uE790", AnnotationToolProperties.Ink),
        new(AnnotationTool.ReadText, "OCR", "O", "\uE8C1", AnnotationToolProperties.Results),
        new(AnnotationTool.Crop, "Crop", "C", "\uE7A8"),
        new(AnnotationTool.Cut, "Cut", "X", "\uE8C6"),
        new(AnnotationTool.Backdrop, "Backdrop", "B", "\uE7F4", AnnotationToolProperties.Variant),
    ];

    public static AnnotationToolDescriptor Get(AnnotationTool tool) =>
        All.First(descriptor => descriptor.Tool == tool);
}
