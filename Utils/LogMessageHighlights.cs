using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Loggepector.Models;

namespace Loggepector.Utils;

internal static class LogMessageHighlights
{
    private static readonly Regex Actor = new(@"(?<key>\*no key\*|[^\s/()]+)/\((?<name>[^)\r\n]+)\)", RegexOptions.Compiled);
    private static readonly Regex Join = new(@"MISC:\s*(?<key>[^\s\\]+)\s*\\\s*(?<name>[^\\\r\n]+)\s*\\", RegexOptions.Compiled);
    private static readonly Regex Recipient = new("""\(\s*PDA: .*? to (?<name>.*?)\) """, RegexOptions.Compiled);

    public static IReadOnlyList<LogMessagePart> Create(ILogEntry entry, string message)
    {
        var identities = new Dictionary<string, LogMessagePartKind>(StringComparer.OrdinalIgnoreCase);
        var parts = new List<LogMessagePart>();
        if (entry is ForcedSayLogEntry && message.StartsWith("[FAI]/", StringComparison.Ordinal))
        {
            parts.Add(new LogMessagePart("AI", LogMessagePartKind.AI));
            parts.Add(new LogMessagePart(" ", LogMessagePartKind.Text));
            message = message[6..];
        }
        foreach (var match in Actor.Matches(entry.GetRawMessage()).Cast<Match>().Concat(Join.Matches(entry.GetRawMessage()).Cast<Match>()))
        {
            var name = match.Groups["name"].Value.Trim();
            var key = match.Groups["key"].Value.Trim();
            message = message.Replace($"{key}/({name})", $"{key} {name}", StringComparison.Ordinal)
                .Replace($"{name}({key})", $"{name} {key}", StringComparison.Ordinal)
                .Replace($"/({name})", $" {name}", StringComparison.Ordinal);
            if (name.Length > 0)
                identities[name] = LogMessagePartKind.CharacterName;
            if (key.Length > 0 && key != "*no key*")
                identities[key] = LogMessagePartKind.CKey;
        }
        var recipient = Recipient.Match(entry.GetRawMessage());
        if (recipient.Success && recipient.Groups["name"].Value.Length > 0)
            identities[recipient.Groups["name"].Value] = LogMessagePartKind.CharacterName;
        if (identities.Count == 0)
        {
            parts.Add(new LogMessagePart(message, LogMessagePartKind.Text));
            return AddChannel(parts, entry.Channel);
        }
        var pattern = @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", identities.Keys.OrderByDescending(value => value.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])";
        var position = 0;
        foreach (Match match in Regex.Matches(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (match.Index > position)
                parts.Add(new LogMessagePart(message[position..match.Index], LogMessagePartKind.Text));
            parts.Add(new LogMessagePart(match.Value, identities[match.Value]));
            position = match.Index + match.Length;
        }
        if (position < message.Length)
            parts.Add(new LogMessagePart(message[position..], LogMessagePartKind.Text));
        return AddChannel(parts, entry.Channel);
    }

    private static IReadOnlyList<LogMessagePart> AddChannel(List<LogMessagePart> parts, string? channel)
    {
        if (channel is null)
            return parts;
        var lastBadge = -1;
        for (var index = 0; index < parts.Count; index++)
        {
            if (parts[index].Kind == LogMessagePartKind.Text && parts[index].Text.Contains('"'))
                break;
            if (parts[index].Kind != LogMessagePartKind.Text)
                lastBadge = index;
        }
        var position = lastBadge + 1;
        if (lastBadge >= 0)
            parts.Insert(position++, new LogMessagePart(" ", LogMessagePartKind.Text));
        parts.Insert(position++, new LogMessagePart(LogDisplayConfig.GetChannel(channel).Text, LogMessagePartKind.Channel, channel));
        if (position == parts.Count || parts[position].Text.Length == 0 || !char.IsWhiteSpace(parts[position].Text[0]))
            parts.Insert(position, new LogMessagePart(" ", LogMessagePartKind.Text));
        return parts;
    }
}
