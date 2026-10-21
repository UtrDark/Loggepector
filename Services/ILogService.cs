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
    string? MapName { get; }
    Task LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<int>? progress = null);
    IReadOnlyList<string> GetTypes();
    IReadOnlyList<string> GetCKeys();
    IReadOnlyDictionary<string, IReadOnlyList<PlayerJoin>> GetPlayerJoins();
    IReadOnlyDictionary<string, IReadOnlyList<PlayerAntagonist>> GetPlayerAntagonists();
    IReadOnlyList<ILogEntry> GetLogs(TimeSpan from, TimeSpan to, params string[] types);
    IReadOnlyList<ILogEntry> GetLogs(TimeSpan from, TimeSpan to, IReadOnlyList<string> types, IReadOnlyList<string> ckeys, string searchText = "");
}
