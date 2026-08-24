using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

public static class ReceiptLifetimePolicy
{
    public static TimeSpan For(CaptureKind? kind) => kind is CaptureKind.Screenshot or CaptureKind.Text or CaptureKind.Link
        ? TimeSpan.FromSeconds(3)
        : TimeSpan.FromSeconds(6);
}
