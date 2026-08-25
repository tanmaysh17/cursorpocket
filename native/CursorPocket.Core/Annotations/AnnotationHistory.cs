namespace CursorPocket.Core.Annotations;

/// <summary>
/// The whole-image geometry: what is kept, what is cut out, and which backdrop is on.
/// </summary>
/// <remarks>
/// Held as one value rather than three fields so a geometry change is a single undo step,
/// and so the current geometry can simply be read back off the history rather than
/// maintained in parallel with it.
/// </remarks>
public sealed record DocumentGeometry(AnnRect? Crop, IReadOnlyList<CutBand> Cuts, int BackdropIndex)
{
    public static DocumentGeometry Default { get; } = new(null, [], 0);

    /// <summary>True when nothing here changes the exported pixel dimensions.</summary>
    public bool IsUntouched => Crop is null && Cuts.Count == 0 && BackdropIndex == 0;
}

/// <summary>One undoable thing the user did.</summary>
public abstract record AnnotationStep;

/// <summary>A mark was drawn.</summary>
public sealed record MarkStep(AnnotationMark Mark) : AnnotationStep;

/// <summary>The image was cropped, cut, or put on a backdrop.</summary>
public sealed record GeometryStep(DocumentGeometry Geometry) : AnnotationStep;

/// <summary>
/// Everything the user has done to a screenshot, with undo and redo.
/// </summary>
/// <remarks>
/// A list plus an index, rather than a stack. The first annotation surface removed the
/// last entry outright on undo, which is why it could never offer redo: the mark was gone,
/// and so was the WinUI element it carried. Keeping the step and moving an index makes redo
/// a matter of walking forward again. Adding after an undo truncates whatever was ahead,
/// which is what keeps redo meaning "the thing I just undid".
///
/// Geometry is derived from the visible steps rather than stored separately, so undoing a
/// crop needs no bookkeeping of its own — the previous geometry simply becomes current
/// again.
/// </remarks>
public sealed class AnnotationHistory
{
    private readonly List<AnnotationStep> _steps = [];
    private int _index;
    private int _nextId = 1;

    /// <summary>Steps currently in effect, oldest first.</summary>
    public IReadOnlyList<AnnotationStep> VisibleSteps => _steps.GetRange(0, _index);

    /// <summary>Marks currently on the image, oldest first.</summary>
    public IReadOnlyList<AnnotationMark> Visible =>
        VisibleSteps.OfType<MarkStep>().Select(step => step.Mark).ToList();

    /// <summary>
    /// The geometry in effect: the newest visible geometry step, or an untouched document.
    /// </summary>
    public DocumentGeometry Geometry
    {
        get
        {
            for (var index = _index - 1; index >= 0; index--)
            {
                if (_steps[index] is GeometryStep geometry)
                {
                    return geometry.Geometry;
                }
            }

            return DocumentGeometry.Default;
        }
    }

    public int VisibleCount => _index;

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _steps.Count;

    /// <summary>True once anything has been done, whether or not it is currently undone.</summary>
    public bool HasMarks => _steps.Count > 0;

    /// <summary>True when a mark has been drawn and is currently in effect.</summary>
    public bool HasVisibleMarks => VisibleSteps.Any(step => step is MarkStep);

    /// <summary>
    /// Hands out the next mark identity. Sequential rather than random so a rebuilt canvas
    /// and a saved image agree on which element belongs to which mark.
    /// </summary>
    public int AllocateId() => _nextId++;

    public void Add(AnnotationMark mark) => Add(new MarkStep(mark));

    public void Add(AnnotationStep step)
    {
        // Anything ahead of the index was undone. Doing something new replaces that future
        // rather than branching, so there is only ever one redo path.
        if (_index < _steps.Count)
        {
            _steps.RemoveRange(_index, _steps.Count - _index);
        }

        _steps.Add(step);
        _index = _steps.Count;
    }

    /// <summary>Takes back the newest step and returns it, or null if there is none.</summary>
    public AnnotationStep? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        _index--;
        return _steps[_index];
    }

    /// <summary>Reapplies the most recently undone step and returns it, or null.</summary>
    public AnnotationStep? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        var step = _steps[_index];
        _index++;
        return step;
    }
}
