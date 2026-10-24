using System;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface IBigLogParser
{
    Task<BigLogData> ParseAsync(string directory, IProgress<int>? progress, CancellationToken cancellationToken);
}
