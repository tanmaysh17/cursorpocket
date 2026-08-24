using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket_App.Services;

public enum ReceiptVisualKind
{
    Capture,
    Error,
    Update,
    Information,
}

public sealed record ReceiptAction(string Label, Func<Task> InvokeAsync);

public sealed record ReceiptRequest(
    CaptureRecord? Record,
    string Title,
    string? Detail = null,
    IReadOnlyList<ReceiptAction>? Actions = null,
    TimeSpan? LifetimeOverride = null,
    ReceiptVisualKind VisualKind = ReceiptVisualKind.Capture)
{
    public TimeSpan Lifetime => LifetimeOverride ?? ReceiptLifetimePolicy.For(Record?.CaptureKind);
}

public interface IReceiptCoordinator : IDisposable
{
    void Show(ReceiptRequest request);
    void Dismiss();
}

public sealed class ReceiptCoordinator(Action openLibrary) : IReceiptCoordinator
{
    private ReceiptWindow? _active;

    public void Show(ReceiptRequest request)
    {
        Dismiss();
        var receipt = new ReceiptWindow(request);
        _active = receipt;
        receipt.OpenLibraryRequested += Receipt_OpenLibraryRequested;
        receipt.Closed += Receipt_Closed;
        receipt.AppWindow.Show(false);
    }

    public void Dismiss()
    {
        var receipt = _active;
        _active = null;
        if (receipt is null) return;
        receipt.OpenLibraryRequested -= Receipt_OpenLibraryRequested;
        receipt.Closed -= Receipt_Closed;
        receipt.Close();
    }

    public void Dispose() => Dismiss();
    private void Receipt_OpenLibraryRequested(object? sender, EventArgs eventArgs) => openLibrary();
    private void Receipt_Closed(object sender, Microsoft.UI.Xaml.WindowEventArgs eventArgs)
    {
        if (ReferenceEquals(_active, sender)) _active = null;
    }
}
