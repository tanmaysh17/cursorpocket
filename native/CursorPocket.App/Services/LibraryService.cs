using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;
using Microsoft.VisualBasic.FileIO;

namespace CursorPocket_App.Services;

public sealed class LibraryService(CaptureStore store) : ILibraryService
{
    public Task<IReadOnlyList<CaptureRecord>> GetRecentAsync(int limit = 250, CancellationToken cancellationToken = default) =>
        store.RecentAsync(limit, cancellationToken);

    public string GetAbsolutePath(CaptureRecord record) => store.AbsolutePath(record);

    public async Task DeleteAsync(CaptureRecord record, CancellationToken cancellationToken = default)
    {
        var path = store.AbsolutePath(record);
        if (File.Exists(path))
        {
            await Task.Run(() => FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.DoNothing), cancellationToken);
        }
        await store.RemoveFromIndexAsync(record.Id, cancellationToken);
    }
}
