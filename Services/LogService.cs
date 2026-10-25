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

public sealed class LogService(IPlayerJoinService playerJoinService, IBigLogService bigLogService) : ILogService
{
    private static readonly Regex LoadingMap = new(
        @"^\[[^\]\r\n]+\] DEBUG: ?Loading (?<map>[^\r\n]+?)\.{3}[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex GarbageShutdown = new(
        @"^\[[^\]\r\n]+\] DEBUG:\s*Shutting down Garbage subsystem\.\.\.[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex DebugObjectStart = new(
        @"^\[[^\]\r\n]+\] DEBUG:\s*\{", RegexOptions.Compiled);
    private Snapshot? _snapshot;

    public int Count => Volatile.Read(ref _snapshot)?.Count ?? 0;

    public DateTime StartTime => (Volatile.Read(ref _snapshot)
        ?? throw new InvalidOperationException("Лог ещё не загружен.")).StartTime;

    public TimeSpan Duration => Volatile.Read(ref _snapshot)?.Duration ?? TimeSpan.Zero;

    public string? MapName => Volatile.Read(ref _snapshot)?.MapName;
    public string? RoundEndHtml => Volatile.Read(ref _snapshot)?.RoundEndHtml;

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0);
        var snapshot = await Task.Run(async () =>
        {
            var parsed = await ParseAsync(path, cancellationToken, progress).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            playerJoinService.Process(parsed.Entries.Where(entry => entry.Type == "MISC"));
            return parsed;
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _snapshot, snapshot);
        progress?.Report(100);
    }

    public IReadOnlyList<string> GetTypes() =>
        Volatile.Read(ref _snapshot)!.Entries.Select(entry => entry.Type).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.Ordinal).ToArray();

    public async Task LoadBigAsync(string path, Func<MapDownloadStage, CancellationToken, Task<bool>> confirmDownload,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        progress?.Report(0);
        var snapshot = await Task.Run(async () =>
        {
            var data = await bigLogService.LoadAsync(path, confirmDownload, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = CreateSnapshot(data.Entries, data.MapName, data.PlayerKeys, data.RoundEndHtml);
            cancellationToken.ThrowIfCancellationRequested();
            playerJoinService.Process(parsed.Entries.Where(entry => entry.Type == "MISC"));
            return parsed;
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref _snapshot, snapshot);
        progress?.Report(100);
    }

    public IReadOnlyList<string> GetCKeys() =>
        Volatile.Read(ref _snapshot)!.Players.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();

    public IReadOnlyDictionary<string, IReadOnlyList<PlayerJoin>> GetPlayerJoins() =>
        playerJoinService.PlayerJoins;

    public IReadOnlyDictionary<string, IReadOnlyList<PlayerAntagonist>> GetPlayerAntagonists() =>
        playerJoinService.PlayerAntagonists;

    public IReadOnlyList<ILogEntry> GetLogs(TimeSpan from, TimeSpan to, params string[] types) =>
        GetLogs(from, to, types, Array.Empty<string>());

    public IReadOnlyList<ILogEntry> GetLogs(TimeSpan from, TimeSpan to, IReadOnlyList<string> types,
        IReadOnlyList<string> ckeys, string searchText = "", bool fuzzy = false, IReadOnlyList<LogMessagePart>? badges = null, CancellationToken cancellationToken = default)
    {
        var snapshot = Volatile.Read(ref _snapshot)!;
        ILogFilter[] filters =
        [
            new TimeLogFilter(snapshot.StartTime, from, to),
            new TypeLogFilter(types),
            new PlayerLogFilter(ckeys, snapshot.Players),
            new TextLogFilter(searchText, fuzzy, badges)
        ];
        IEnumerable<ILogEntry> entries = snapshot.Entries;
        for (var index = 0; index < filters.Length; index++)
        {
            entries = filters[index].Apply(entries);
            if (index == 0 && cancellationToken.CanBeCanceled)
                entries = entries.Where(entry =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                });
        }
        return entries.ToArray();
    }

    private static async Task<Snapshot> ParseAsync(string path, CancellationToken cancellationToken, IProgress<int>? progress)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var blocks = new List<(string Text, int Line)>();
        string? entryText = null;
        StringBuilder? text = null;
        var entryStartLine = 0;
        var sourceLineNumber = 0;
        var fileLength = stream.Length;
        var garbageReportPending = false;
        var skipEntry = false;
        var reportedProgress = -1;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (line is null || line.StartsWith('['))
            {
                if (!skipEntry && entryText is not null)
                    blocks.Add((text?.ToString() ?? entryText, entryStartLine));
                if (line is null)
                    break;
                entryStartLine = sourceLineNumber + 1;
                entryText = line;
                text = null;
                skipEntry = garbageReportPending && DebugObjectStart.IsMatch(line);
                garbageReportPending = GarbageShutdown.IsMatch(line);
            }
            else if (entryText is not null && !skipEntry)
            {
                text ??= new StringBuilder(entryText);
                text.Append('\n').Append(line);
            }
            sourceLineNumber++;
            var currentProgress = (int)(stream.Position * 25 / Math.Max(1, fileLength));
            if (currentProgress != reportedProgress)
            {
                progress?.Report(currentProgress);
                reportedProgress = currentProgress;
            }
        }
        var parsed = new ILogEntry?[blocks.Count];
        var completed = 0;
        var progressGate = new object();
        Parallel.For(0, blocks.Count, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 4)
        }, index =>
        {
            var block = blocks[index];
            parsed[index] = LogEntryParser.Parse(block.Text, block.Line);
            var done = Interlocked.Increment(ref completed);
            if (progress is not null && (done & 255) == 0)
                lock (progressGate)
                {
                    var current = 25 + (int)((long)Volatile.Read(ref completed) * 55 / Math.Max(1, blocks.Count));
                    if (current > reportedProgress)
                    {
                        progress.Report(current);
                        reportedProgress = current;
                    }
                }
        });
        var entries = new List<ILogEntry>(parsed.Length);
        foreach (var entry in parsed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is null)
                continue;
            if (entries.Count > 0 && MergeRadio.Merge(entries[^1], entry) is { } merged)
                entries[^1] = merged;
            else
                entries.Add(entry);
        }
        progress?.Report(90);

        var roundEndPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "round_end_data.html");
        var roundEndHtml = File.Exists(roundEndPath)
            ? await File.ReadAllTextAsync(roundEndPath, cancellationToken).ConfigureAwait(false) : null;
        return CreateSnapshot(entries, roundEndHtml: roundEndHtml);
    }

    private static Snapshot CreateSnapshot(IReadOnlyList<ILogEntry> entries, string? detectedMap = null, IReadOnlyList<string>? playerKeys = null, string? roundEndHtml = null)
    {
        var sorted = entries.Select(entry => entry is ForcedSayLogEntry || entry.GetRawMessage().Contains("[FAI]", StringComparison.Ordinal)
                ? ((LogEntry)entry) with { Type = "AI" } : entry)
            .OrderBy(entry => entry.Timestamp).ThenBy(entry => entry.LineNumber)
            .ToArray();
        var startTime = sorted.Length == 0 ? default : sorted[0].Timestamp;
        var tickets = new TicketLogParser();
        sorted = sorted.Select(tickets.Parse).OfType<ILogEntry>().ToArray();
        var duration = sorted.Length == 0 ? TimeSpan.Zero : sorted[^1].Timestamp - startTime;
        var players = entries.Where(entry => entry.Type == "ACCESS")
            .Select(entry => Regex.Match(entry.GetRawMessage(), @"^\[[^\]\r\n]+\] ACCESS: ?Mob Login: (?<key>.+?)/\("))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .Concat(playerKeys ?? Array.Empty<string>())
            .Where(key => key != "*no key*")
            .GroupBy(CKey.Normalize)
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => new Regex(
                @"(?<![\p{L}\p{N}_-])(?:" +
                string.Join("|", group.Append(group.Key).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(key => key.Length).Select(Regex.Escape)) +
                @")(?![\p{L}\p{N}_-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        Regex? sensitiveKeys = null;
        IReadOnlySet<string>? characterNames = null;
        Parallel.Invoke(
            () => sensitiveKeys = LogMessageSanitizer.CreateKeyPattern(entries),
            () => characterNames = LogMessageHighlights.GetCharacterNames(entries));
        sorted = sorted.Select(entry => ((LogEntry)entry) with { SensitiveKeys = sensitiveKeys, CharacterNames = characterNames }).ToArray();
        var mapName = sorted.Where(entry => entry.Type == "DEBUG")
            .Select(entry => LoadingMap.Match(entry.GetRawMessage())).FirstOrDefault(match => match.Success)
            ?.Groups["map"].Value;
        return new Snapshot(startTime, duration, sorted.Length, sorted, players, mapName ?? detectedMap, roundEndHtml);
    }

    private sealed record Snapshot(DateTime StartTime, TimeSpan Duration, int Count, ILogEntry[] Entries,
        Dictionary<string, Regex> Players, string? MapName, string? RoundEndHtml);
}
