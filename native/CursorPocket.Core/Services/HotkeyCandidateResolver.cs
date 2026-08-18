namespace CursorPocket.Core.Services;

public static class HotkeyCandidateResolver
{
    private static readonly string[] Fallbacks = ["Ctrl+Shift+Space", "Win+Alt+Space", "Ctrl+Alt+Space"];

    public static string? RegisterFirstAvailable(string preferred, Func<string, bool> tryRegister)
    {
        ArgumentNullException.ThrowIfNull(tryRegister);
        foreach (var candidate in new[] { preferred }.Concat(Fallbacks).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (tryRegister(candidate))
            {
                return candidate;
            }
        }
        return null;
    }
}
