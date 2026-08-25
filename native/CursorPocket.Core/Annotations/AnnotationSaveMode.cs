namespace CursorPocket.Core.Annotations;

/// <summary>Where a save lands.</summary>
public enum AnnotationSaveMode
{
    /// <summary>Replace the capture in place, keeping one capture, one file, one row.</summary>
    Overwrite,

    /// <summary>Leave the capture alone and write the result as a new one.</summary>
    NewCapture,
}

/// <summary>Which entry point the editor was opened from.</summary>
public enum AnnotationOrigin
{
    /// <summary>Opened straight after the shot was taken, before any receipt appeared.</summary>
    FreshCapture,

    /// <summary>Opened on something already kept — the Library, a receipt, a pin, a file.</summary>
    ExistingCapture,
}

/// <summary>
/// Decides whether a save replaces the capture or writes a new one.
/// </summary>
/// <remarks>
/// Marks are additive: the pixels underneath a box are still there, so overwriting costs
/// nothing that was not already visible, and it keeps one capture to one file and one
/// Library row. Crop and cut <i>delete</i> pixels and a backdrop changes the dimensions,
/// and because a save overwrites rather than deleting there is no Recycle Bin copy to fall
/// back on — so a geometry change writes a new capture and leaves the original alone.
/// That also means the new record carries correct width, height, and preview text, so
/// nothing has to go back and repair a stale index entry.
/// </remarks>
public static class SaveTarget
{
    public static AnnotationSaveMode For(bool marksChanged, bool geometryChanged, AnnotationOrigin origin)
    {
        // Anything already kept is an artifact the user chose. Silently rewriting it is
        // destructive in a way that overwriting a shot taken two seconds ago is not.
        if (origin == AnnotationOrigin.ExistingCapture)
        {
            return AnnotationSaveMode.NewCapture;
        }

        return geometryChanged ? AnnotationSaveMode.NewCapture : AnnotationSaveMode.Overwrite;
    }

    /// <summary>Wording for the receipt, so the user is told which of the two happened.</summary>
    public static string Describe(AnnotationSaveMode mode, bool copied) => mode switch
    {
        AnnotationSaveMode.NewCapture => copied ? "Edited copy saved · copied" : "Edited copy saved",
        _ => copied ? "Screenshot saved · copied" : "Screenshot saved",
    };
}
