using System;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface IRoundEndStyleService
{
    bool HasLocalData { get; }
    void DeleteAll();
    Task<string?> LoadAsync(string repository, Func<MapDownloadStage, CancellationToken, Task<bool>> confirmDownload,
        CancellationToken cancellationToken);
}
