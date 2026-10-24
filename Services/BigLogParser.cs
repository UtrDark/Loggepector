using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.Services;

public sealed class BigLogParser : IBigLogParser
{
    internal static readonly string[] FileNames =
    {
        "game.log.json", "attack.log.json", "pda.log.json", "manifest.log.json", "dynamic.log.json",
        "telecomms.log.json", "admin.log.json", "adminprivate.log.json", "debug.log.json", "href.log.json", "dd.log", "round_end_data.html"
    };

    private static readonly Dictionary<string, string> Categories = new(StringComparer.Ordinal)
    {
        ["game"] = "GAME", ["game-access"] = "ACCESS", ["game-say"] = "SAY", ["game-whisper"] = "WHISPER",
        ["game-ooc"] = "OOC", ["game-looc"] = "LOOC", ["game-emote"] = "EMOTE", ["attack"] = "ATTACK",
        ["pda"] = "PDA", ["manifest"] = "MISC", ["dynamic"] = "MISC", ["telecomms"] = "RADIO",
        ["admin"] = "ADMIN", ["adminprivate"] = "ADMINPRIVATE", ["admin-msay"] = "ADMIN",
        ["debug-mapping"] = "DEBUG", ["debug-mobtag"] = "DEBUG", ["debug-sql"] = "DEBUG",
        ["href-tgui"] = "MISC", ["href"] = "MISC"
    };
    private static readonly Regex EmbeddedType = new(@"^(?:ADMINPRIVATE|ADMIN PRIVATE|ADMIN|MISC|GAME|ACCESS|SAY|RADIO|LOOC|OOC|EMOTE|ATTACK|DEBUG):", RegexOptions.Compiled);
    private static readonly Regex Player = new(@"(?<key>\*no key\*|[^\s/()]+)/\(", RegexOptions.Compiled);
    private static readonly Regex Login = new(@"^(?:Login|Logout):\s*(?<key>[^\s/]+)", RegexOptions.Compiled);
    private static readonly Regex LoadingMap = new(@"^Loading (?<map>[^\r\n]+?)\.{3}[ \t]*$", RegexOptions.Compiled);

    public async Task<BigLogData> ParseAsync(string directory, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        CollectFiles(directory, files, cancellationToken);
        var jsonFiles = files.Where(path => path.EndsWith(".log.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (jsonFiles.Length == 0)
            throw new InvalidDataException("В папке нет поддерживаемых .log.json файлов.");
        var totalBytes = jsonFiles.Sum(path => new FileInfo(path).Length);
        long completedBytes = 0;
        var reported = -1;
        var entries = new List<ILogEntry>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<long>();
        var roundIds = new HashSet<string>(StringComparer.Ordinal);
        var sequence = 0;
        foreach (var path in jsonFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            var lineNumber = 0;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var record = document.RootElement;
                    if (record.TryGetProperty("secret", out var secret) &&
                        (secret.ValueKind == JsonValueKind.True || secret.ValueKind == JsonValueKind.Number && secret.GetInt32() != 0))
                        break;
                    if (record.TryGetProperty("round-id", out var roundId))
                    {
                        roundIds.Add(roundId.ToString());
                        if (roundIds.Count > 1)
                            throw new InvalidDataException("Выбрана папка с логами нескольких раундов.");
                    }
                    if (!record.TryGetProperty("msg", out var messageElement))
                        continue;
                    var category = record.GetProperty("cat").GetString()!;
                    var message = messageElement.GetString() ?? throw new InvalidDataException("Отсутствует текст записи.");
                    string? type;
                    if (category == "game-compat")
                    {
                        if (!EmbeddedType.IsMatch(message))
                            continue;
                        type = null;
                    }
                    else if (!Categories.TryGetValue(category, out type))
                        continue;
                    var timestamp = record.GetProperty("ts").GetString();
                    if (!DateTime.TryParseExact(timestamp, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var time))
                        throw new InvalidDataException("Некорректное время записи.");
                    if (record.TryGetProperty("id", out var id) && !ids.Add(id.GetInt64()))
                        continue;
                    if (category == "manifest")
                    {
                        message = message.Replace("\\\\", "\\", StringComparison.Ordinal);
                        var separator = message.IndexOf('\\');
                        if (separator > 0)
                            keys.Add(message[..separator].Trim());
                    }
                    foreach (Match actor in Player.Matches(message))
                        if (actor.Groups["key"].Value != "*no key*")
                            keys.Add(actor.Groups["key"].Value.Replace("[DC]", string.Empty, StringComparison.OrdinalIgnoreCase));
                    if (category == "game-access" && Login.Match(message) is { Success: true } login)
                        keys.Add(login.Groups["key"].Value.Replace("[DC]", string.Empty, StringComparison.OrdinalIgnoreCase));
                    var header = time.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    var text = type is null ? $"[{header}] {message}" : $"[{header}] {type}: {message}";
                    var entry = LogEntryParser.Parse(text, ++sequence)
                        ?? throw new InvalidDataException("Не удалось разобрать запись.");
                    entries.Add(entry);
                }
                catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or KeyNotFoundException or InvalidDataException)
                {
                    throw new InvalidDataException($"{Path.GetFileName(path)}, строка {lineNumber}: {exception.Message}", exception);
                }
                var current = (int)((completedBytes + stream.Position) * 90 / Math.Max(1, totalBytes));
                if (current != reported)
                {
                    reported = current;
                    progress?.Report(current);
                }
            }
            completedBytes += stream.Length;
        }
        var sorted = entries.OrderBy(entry => entry.Timestamp).ThenBy(entry => entry.LineNumber).ToArray();
        var merged = new List<ILogEntry>(sorted.Length);
        foreach (var entry in sorted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (merged.Count > 0 && MergeRadio.Merge(merged[^1], entry) is { } radio)
                merged[^1] = radio;
            else
                merged.Add(entry);
        }
        string? mapName = null;
        foreach (var path in files.Where(path => Path.GetFileName(path).Equals("dd.log", StringComparison.OrdinalIgnoreCase)))
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false, true), true);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (LoadingMap.Match(line) is not { Success: true } map)
                    continue;
                mapName = map.Groups["map"].Value.Trim();
                break;
            }
            if (mapName is not null)
                break;
        }
        string? roundEndHtml = null;
        foreach (var path in files.Where(path => Path.GetFileName(path).Equals("round_end_data.html", StringComparison.OrdinalIgnoreCase)))
        {
            var html = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            roundEndHtml = html;
        }
        progress?.Report(90);
        return new BigLogData(merged, mapName, keys.ToArray(), roundEndHtml);
    }

    private static void CollectFiles(string directory, List<string> files, CancellationToken cancellationToken)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.Name.Equals("secret", StringComparison.OrdinalIgnoreCase))
                continue;
            if (entry is DirectoryInfo child)
                CollectFiles(child.FullName, files, cancellationToken);
            else if (FileNames.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
                files.Add(entry.FullName);
        }
    }
}
