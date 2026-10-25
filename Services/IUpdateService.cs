using System;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface IUpdateService
{
    Task<ReleaseUpdate> CheckAsync(Func<CancellationToken, Task<bool?>> requestConsent, CancellationToken cancellationToken);
}
