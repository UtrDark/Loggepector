using System;
using System.Net;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public interface ILogisService
{
    void RestoreSession();
    string LoadAddress();
    void SaveAddress(Uri address);
    Uri? ServerAddress { get; }
    string? SessionId { get; }
    string? SessionCookieName { get; }
    void SetSession(Uri address, Cookie cookie);
    Task<IReadOnlyList<string>> GetServersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetDatesAsync(string server, CancellationToken cancellationToken);
    Task<IReadOnlyList<LogisLogFile>> GetLogsAsync(string server, string date, CancellationToken cancellationToken);
    Task DownloadGameLogAsync(string server, string date, LogisLogFile log, string destination,
        IProgress<int> progress, CancellationToken cancellationToken);
}
