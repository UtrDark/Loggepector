using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;
using Loggepector.Filters;
using Loggepector.Utils;

namespace Loggepector.Services;

public sealed class LogService : ILogService
{
    private Snapshot? _snapshot;

    public int Count => Volatile.Read(ref _snapshot)?.Count ?? 0;

    public TimeSpan Duration => Volatile.Read(ref _snapshot)?.Duration ?? TimeSpan.Zero;

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0);
        var snapshot = await Task.Run(() => ParseAsync(path, cancellationToken, progress), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _snapshot, snapshot);
        progress?.Report(100);
    }

    public IReadOnlyList<string> GetTypes() =>
        Volatile.Read(ref _snapshot)!.Entries.Select(entry => entry.Type).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<string> GetCKeys() =>
        Volatile.Read(ref _snapshot)!.Players.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<ILogEntry> GetLogs(TimeSpan from, TimeSpan to, params string[] types) =>
        GetLogs(from, to, types, Array.Empty<string>());

    public IReadOnlyList<ILogEntry> GetLogs(TimeSpan from, TimeSpan to, IReadOnlyList<string> types,
        IReadOnlyList<string> ckeys, string searchText = "")
    {
        var snapshot = Volatile.Read(ref _snapshot)!;
        ILogFilter[] filters =
        [
            new TimeLogFilter(snapshot.StartTime, from, to),
            new TypeLogFilter(types),
            new PlayerLogFilter(ckeys, snapshot.Players),
            new TextLogFilter(searchText)
        ];
        IEnumerable<ILogEntry> entries = snapshot.Entries;
        foreach (var filter in filters)
            entries = filter.Apply(entries);
        return entries.ToArray();
    }

    private static async Task<Snapshot> ParseAsync(string path, CancellationToken cancellationToken, IProgress<int>? progress)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var entries = new List<ILogEntry>();
        var text = new StringBuilder();
        var entryStartLine = 0;
        var sourceLineNumber = 0;
        var fileLength = stream.Length;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (line is null || line.StartsWith('['))
            {
                if (entryStartLine != 0 && LogEntryParser.Parse(text.ToString(), entryStartLine) is { } entry)
                {
                    if (entries.Count > 0 && MergeRadio.Merge(entries[^1], entry) is { } merged)
                        entries[^1] = merged;
                    else
                        entries.Add(entry);
                }

                if (line is null)
                    break;

                entryStartLine = sourceLineNumber + 1;
                text.Clear();
            }

            sourceLineNumber++;
            if (entryStartLine != 0)
            {
                if (text.Length > 0)
                    text.Append('\n');

                text.Append(line);
            }

            progress?.Report((int)Math.Floor(stream.Position * 90.0 / fileLength));
        }

        var sorted = entries.OrderBy(entry => entry.Timestamp).ThenBy(entry => entry.LineNumber)
            .ToArray();
        var startTime = sorted.Length == 0 ? default : sorted[0].Timestamp;
        var duration = sorted.Length == 0 ? TimeSpan.Zero : sorted[^1].Timestamp - startTime;
        var players = entries.Where(entry => entry.Type == "ACCESS")
            .Select(entry => Regex.Match(entry.GetRawMessage(), @"^\[[^\]\r\n]+\] ACCESS: ?Mob Login: (?<key>.+?)/\("))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .Where(key => key != "*no key*")
            .GroupBy(CKey.Normalize)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => new Regex(
                @"(?<![\p{L}\p{N}_-])(?:" +
                string.Join("|", group.Append(group.Key).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(key => key.Length).Select(Regex.Escape)) +
                @")(?![\p{L}\p{N}_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        var sensitiveKeys = LogMessageSanitizer.CreateKeyPattern(entries);
        sorted = sorted.Select(entry => ((LogEntry)entry) with { SensitiveKeys = sensitiveKeys }).ToArray();
        progress?.Report(100);
        return new Snapshot(startTime, duration, entries.Count, sorted, players);
    }

    private sealed record Snapshot(DateTime StartTime, TimeSpan Duration, int Count, ILogEntry[] Entries,
        Dictionary<string, Regex> Players);
}
