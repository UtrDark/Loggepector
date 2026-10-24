using System;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface IBigLogService
{
    Task<BigLogData> LoadAsync(string path, Func<MapDownloadStage, CancellationToken, Task<bool>> confirmDownload,
        IProgress<int>? progress, CancellationToken cancellationToken);
}
