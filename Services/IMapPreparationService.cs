using System;
using System.Threading;
using System.Threading.Tasks;

namespace Loggepector.Services;

public interface IMapPreparationService
{
    Task<bool?> GetChoiceAsync(CancellationToken cancellationToken = default);
    Task SaveChoiceAsync(bool enabled, CancellationToken cancellationToken = default);
    Task PrepareAsync(IProgress<string> progress, CancellationToken cancellationToken = default);
}
