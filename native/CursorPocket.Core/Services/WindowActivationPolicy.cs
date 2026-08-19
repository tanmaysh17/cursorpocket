namespace CursorPocket.Core.Services;

public static class WindowActivationPolicy
{
    public static bool ShouldIssueRestore(bool isCurrentlyMinimized) => isCurrentlyMinimized;
}
