namespace CursorPocket.Core.Annotations;

/// <summary>
/// The marks on a screenshot, with undo and redo.
/// </summary>
/// <remarks>
/// A list plus an index, rather than a stack. The first annotation surface removed the
/// last entry outright on undo, which is why it could never offer redo: the mark was
/// gone, and so was the WinUI element it carried. Keeping the mark and moving an index
/// makes redo a matter of walking forward again. Adding a mark after an undo truncates
/// whatever was ahead, which is what keeps redo meaning "the thing I just undid".
/// </remarks>
public sealed class AnnotationHistory
{
    private readonly List<AnnotationMark> _marks = [];
    private int _index;
    private int _nextId = 1;

    /// <summary>Marks currently on the image, oldest first.</summary>
    public IReadOnlyList<AnnotationMark> Visible => _marks.GetRange(0, _index);

    public int VisibleCount => _index;

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _marks.Count;

    /// <summary>True once anything has been drawn, whether or not it is currently undone.</summary>
    public bool HasMarks => _marks.Count > 0;

    /// <summary>
    /// Hands out the next mark identity. Sequential rather than random so a rebuilt
    /// canvas and a saved image agree on which element belongs to which mark.
    /// </summary>
    public int AllocateId() => _nextId++;

    public void Add(AnnotationMark mark)
    {
        // Anything ahead of the index was undone. Drawing something new replaces that
        // future rather than branching, so there is only ever one redo path.
        if (_index < _marks.Count)
        {
            _marks.RemoveRange(_index, _marks.Count - _index);
        }

        _marks.Add(mark);
        _index = _marks.Count;
    }

    /// <summary>Hides the newest visible mark and returns it, or null if there is none.</summary>
    public AnnotationMark? Undo()
    {
        if (!CanUndo)
        {
            return null;
        }

        _index--;
        return _marks[_index];
    }

    /// <summary>Brings back the most recently undone mark and returns it, or null.</summary>
    public AnnotationMark? Redo()
    {
        if (!CanRedo)
        {
            return null;
        }

        var mark = _marks[_index];
        _index++;
        return mark;
    }
}
