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
    private static readonly Regex Area = new(@"\((?<area>[^()]+?)\(\s*-?\d+\s*,\s*-?\d+\s*,\s*-?\d+\s*\)\s*\)", RegexOptions.Compiled);
    private static readonly Regex Mob = new(@"\s*\(mob_\d+\)", RegexOptions.Compiled);
    private static readonly Regex NoKey = new(@"(?:\(\s*\*no key\*\s*\)|\*no key\*)[ \t]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ActingAs = new(@"(?:(?<![\w:])(?<key>[A-Za-z0-9_@-]+)\s+)?\(as (?<name>.*?) at -?\d+\s*,\s*-?\d+\s*,\s*-?\d+\)", RegexOptions.Compiled);
    private static readonly Regex SpellTarget = new(@"\bcast the spell .+? on / at (?<name>.+?)\.[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex PointTarget = new(@"\bpoints at (?<name>.+?)[ \t]*$", RegexOptions.Compiled);

    internal static IReadOnlySet<string> GetCharacterNames(IEnumerable<ILogEntry> entries) => entries
        .SelectMany(entry => Actor.Matches(entry.GetRawMessage()).Cast<Match>()
            .Concat(ActingAs.Matches(entry.GetRawMessage()).Cast<Match>())
            .Concat(Join.Matches(entry.GetRawMessage()).Cast<Match>()))
        .Select(match => match.Groups["name"].Value.Trim())
        .Where(name => name.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<LogMessagePart> Create(ILogEntry entry, string message, bool showArea = false)
    {
        message = EmojiMarkup.Replace(message);
        var locations = Area.Matches(message).Cast<Match>().ToArray();
        var inlineAreas = entry is ExplosionLogEntry || locations.Length > 1 || message.Contains("has teleported from", StringComparison.OrdinalIgnoreCase);
        static string AreaName(Match match) => match.Groups["area"].Value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        message = Mob.Replace(Area.Replace(message, match => inlineAreas ? AreaName(match) : string.Empty), string.Empty).TrimEnd();
        var identities = new Dictionary<string, LogMessagePartKind>(StringComparer.OrdinalIgnoreCase);
        if (entry is LogEntry { CharacterNames: { } names } && (entry.Type is "ATTACK" or "EMOTE"))
        {
            var source = Area.Replace(entry.GetRawMessage(), string.Empty).TrimEnd();
            var target = (entry.Type == "ATTACK" ? SpellTarget : PointTarget).Match(source);
            if (target.Success && names.Contains(target.Groups["name"].Value.Trim()))
                identities[target.Groups["name"].Value.Trim()] = LogMessagePartKind.CharacterName;
        }
        if (showArea && inlineAreas)
            foreach (var location in locations)
                identities[AreaName(location)] = LogMessagePartKind.Area;
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
        message = NoKey.Replace(message, string.Empty).TrimStart();
        var recipient = Recipient.Match(entry.GetRawMessage());
        foreach (Match actor in ActingAs.Matches(entry.GetRawMessage()))
        {
            if (actor.Groups["name"].Value.Length > 0)
                identities[actor.Groups["name"].Value] = LogMessagePartKind.CharacterName;
            if (actor.Groups["key"].Success)
                identities[actor.Groups["key"].Value] = LogMessagePartKind.CKey;
        }
        if (recipient.Success && recipient.Groups["name"].Value.Length > 0)
            identities[recipient.Groups["name"].Value] = LogMessagePartKind.CharacterName;
        if (entry is AdminPrivateLogEntry ticket)
            foreach (var highlight in ticket.Highlights)
                identities[highlight.Key] = highlight.Value;
        if (entry is AttackLogEntry attack)
            foreach (var highlight in attack.Highlights)
                if (highlight.Key.Length > 0 && (highlight.Value != LogMessagePartKind.Area || showArea))
                    identities[highlight.Key] = highlight.Value;
        if (entry is ExplosionLogEntry explosion)
            foreach (var highlight in explosion.Highlights)
                identities[highlight.Key] = highlight.Value;
        if (identities.Count == 0)
        {
            parts.Add(new LogMessagePart(message, LogMessagePartKind.Text));
            return Finish(parts, entry, showArea && !inlineAreas);
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
        return Finish(parts, entry, showArea && !inlineAreas);
    }

    private static IReadOnlyList<LogMessagePart> Finish(List<LogMessagePart> parts, ILogEntry entry, bool showArea)
    {
        AddChannel(parts, entry.Channel);
        if (showArea)
        {
            foreach (Match area in Area.Matches(entry.GetRawMessage()))
            {
                parts.Add(new LogMessagePart(" ", LogMessagePartKind.Text));
                parts.Add(new LogMessagePart(area.Groups["area"].Value.Trim(), LogMessagePartKind.Area));
            }
        }
        var result = new List<LogMessagePart>();
        foreach (var part in parts)
        {
            if (part.Kind != LogMessagePartKind.Text)
            {
                result.Add(part);
                continue;
            }
            var offset = 0;
            foreach (Match emoji in EmojiMarkup.Token.Matches(part.Text))
            {
                if (emoji.Index > offset)
                    result.Add(new LogMessagePart(part.Text[offset..emoji.Index], LogMessagePartKind.Text));
                result.Add(new LogMessagePart(emoji.Value, LogMessagePartKind.Emoji, emoji.Groups["name"].Value));
                offset = emoji.Index + emoji.Length;
            }
            if (offset < part.Text.Length)
                result.Add(new LogMessagePart(part.Text[offset..], LogMessagePartKind.Text));
        }
        return result;
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
