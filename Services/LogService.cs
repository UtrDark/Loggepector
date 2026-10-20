using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;
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
        Volatile.Read(ref _snapshot)!.ByType.Keys.OrderBy(type => type, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<LogEntry> GetLogs(TimeSpan from, TimeSpan to, params string[] types)
    {
        var snapshot = Volatile.Read(ref _snapshot)!;
        var arrays = new List<LogEntry[]>();
        if (types.Length == 0)
        {
            arrays.AddRange(snapshot.ByType.Values);
        }
        else
        {
            foreach (var type in types.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (snapshot.ByType.TryGetValue(type, out var entries))
                    arrays.Add(entries);
            }
        }

        var queue = new PriorityQueue<(LogEntry[] Entries, int Index, int End), (DateTime Time, int Line)>();
        foreach (var entries in arrays)
        {
            var start = LogSearch.FindBoundary(entries, snapshot.StartTime, from, false);
            var end = LogSearch.FindBoundary(entries, snapshot.StartTime, to, true);
            if (start < end)
                queue.Enqueue((entries, start, end), (entries[start].Timestamp, entries[start].LineNumber));
        }

        var result = new List<LogEntry>();
        while (queue.TryDequeue(out var cursor, out _))
        {
            result.Add(cursor.Entries[cursor.Index]);
            var next = cursor.Index + 1;
            if (next < cursor.End)
                queue.Enqueue((cursor.Entries, next, cursor.End), (cursor.Entries[next].Timestamp, cursor.Entries[next].LineNumber));
        }

        return result.AsReadOnly();
    }

    private static async Task<Snapshot> ParseAsync(string path, CancellationToken cancellationToken, IProgress<int>? progress)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var entries = new List<LogEntry>();
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
                    entries.Add(entry);

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

        var byType = entries.GroupBy(entry => entry.Type)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(entry => entry.Timestamp).ThenBy(entry => entry.LineNumber).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var startTime = entries.Count == 0 ? default : entries.Min(entry => entry.Timestamp);
        var duration = entries.Count == 0 ? TimeSpan.Zero : byType.Values.Max(group => group[^1].Timestamp) - startTime;
        progress?.Report(100);
        return new Snapshot(startTime, duration, entries.Count, byType);
    }

    private sealed record Snapshot(DateTime StartTime, TimeSpan Duration, int Count, Dictionary<string, LogEntry[]> ByType);
}
