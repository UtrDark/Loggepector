using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface ILogService
{
    int Count { get; }
    TimeSpan Duration { get; }
    Task LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<int>? progress = null);
    IReadOnlyList<string> GetTypes();
    IReadOnlyList<LogEntry> GetLogs(TimeSpan from, TimeSpan to, params string[] types);
}
