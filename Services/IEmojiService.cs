using System;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface IEmojiService
{
    bool HasLocalData { get; }
    Task PrepareAsync(string repository, Func<MapDownloadStage, CancellationToken, Task<bool>> confirm, CancellationToken token);
    void DeleteAll();
}
