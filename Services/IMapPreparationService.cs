using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface IMapPreparationService
{
    bool HasLocalData { get; }
    Task DeleteAllAsync(CancellationToken cancellationToken = default);
    Task<bool?> GetChoiceAsync(CancellationToken cancellationToken = default);
    Task<string> GetRepositoryAsync(CancellationToken cancellationToken = default);
    Task SaveChoiceAsync(bool enabled, string repositoryUrl, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetMapsAsync(CancellationToken cancellationToken = default);
    Task RenderAsync(string mapName, IProgress<string> progress, CancellationToken cancellationToken = default);
    Task PrepareAsync(IProgress<string> progress, Func<MapDownloadStage, CancellationToken, Task> confirmDownload,
        CancellationToken cancellationToken = default);
}
