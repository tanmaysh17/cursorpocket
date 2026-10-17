namespace CursorPocket.Core.Annotations;

/// <summary>
/// Decides how dim focus regions share one darkened layer.
/// </summary>
/// <remarks>
/// Every dim focus on an image punches its hole in the same layer rather than drawing a
/// layer of its own. Separate layers stacked: the second darkened the first region and
/// doubled the dim everywhere else, so spotlighting two things was impossible. The layer
/// is drawn where the first dim focus sits in mark order, so marks placed before it are
/// dimmed and marks placed after it are not, exactly as with a single focus. Both the
/// canvas and the exporter ask this class, so they cannot pick different owners.
/// </remarks>
public static class FocusDimming
{
    /// <summary>The dim focus marks among <paramref name="marks"/>, in order.</summary>
    public static IReadOnlyList<FocusMark> Regions(IEnumerable<AnnotationMark> marks) =>
        marks.OfType<FocusMark>().Where(focus => focus.Mode == FocusMode.Dim).ToList();

    /// <summary>Whether <paramref name="focus"/> is the one that draws the shared layer.</summary>
    public static bool OwnsLayer(FocusMark focus, IReadOnlyList<FocusMark> regions) =>
        regions.Count > 0 && regions[0].Id == focus.Id;
}
