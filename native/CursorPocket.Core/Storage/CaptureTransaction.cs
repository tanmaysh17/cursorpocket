using CursorPocket.Core.Models;

namespace CursorPocket.Core.Storage;

public sealed record CaptureTransactionRequest(
    CaptureKind Kind,
    string Extension,
    string Preview,
    IReadOnlyDictionary<string, object?>? Metadata = null);

public sealed record CaptureTransactionResult(CaptureRecord Record, string AbsolutePath);

public interface ICaptureTransaction
{
    Task<CaptureTransactionResult> CommitAsync(
        CaptureTransactionRequest request,
        Func<string, CancellationToken, Task> write,
        CancellationToken cancellationToken = default);
}

public sealed class CaptureTransaction(CaptureStore store) : ICaptureTransaction
{
    public Task<CaptureTransactionResult> CommitAsync(
        CaptureTransactionRequest request,
        Func<string, CancellationToken, Task> write,
        CancellationToken cancellationToken = default) =>
        CommitReservedAsync(store.Reserve(request.Kind, request.Extension), request, write, cancellationToken);

    /// <summary>
    /// Writes into a reservation taken earlier. A screenshot reserves first so its final
    /// path is known while the pixels are still in memory, which lets the editor open on
    /// those pixels without waiting for the PNG to reach the disk.
    /// </summary>
    public async Task<CaptureTransactionResult> CommitReservedAsync(
        CaptureReservation reservation,
        CaptureTransactionRequest request,
        Func<string, CancellationToken, Task> write,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(reservation.AbsolutePath)!;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(reservation.AbsolutePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await write(temporaryPath, cancellationToken);
            var info = new FileInfo(temporaryPath);
            if (!info.Exists || info.Length == 0)
            {
                throw new InvalidDataException("The capture writer did not produce a complete file.");
            }
            File.Move(temporaryPath, reservation.AbsolutePath);
            var record = await store.RegisterReservationAsync(reservation, request.Preview, request.Metadata, cancellationToken);
            return new CaptureTransactionResult(record, reservation.AbsolutePath);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch (IOException) { }
        }
    }
}
