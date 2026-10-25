using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Loggepector.Models;

namespace Loggepector.Utils;

public static class LogEntryParser
{
    private static readonly Regex Channel = new(
        @"^(?<speaker>.+? \(mob_\d+\)) \[(?<channel>[^\]\r\n]+)\](?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex Announcement = new(
        @"^.+?/\(.*?\)(?: \(mob_\d+\))? \((?:station announcement from\b|priority announcement\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SystemRadioChannel = new(
        @"^(?<speaker>.+?) \[(?<channel>\d{3}\.\d)\](?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static ILogEntry? Parse(string text, int lineNumber)
    {
        if (text.Length < 26 || text[0] != '[' || text[24] != ']' || text[25] != ' ' ||
            !DateTime.TryParseExact(text.AsSpan(1, 23),
                "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return null;

        var start = 26;
        var type = string.Empty;
        if (text.Length > start && text[start] is >= 'A' and <= 'Z')
        {
            var end = start + 1;
            while (end < text.Length && (text[end] is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or ' '))
                end++;
            if (end < text.Length && text[end] == ':')
            {
                type = text[start..end];
                start = end + 1;
                if (start < text.Length && text[start] == ' ')
                    start++;
            }
        }
        var message = text[start..];
        if (type == "GAME" && message.Contains(" has attempted to say forbidden word.", StringComparison.OrdinalIgnoreCase))
            type = "FORBIDDEN";
        string? channel = null;
        if ((type is "SAY" or "RADIO" or "MISC") && message.Contains(" [", StringComparison.Ordinal))
        {
            var channelMatch = Channel.Match(message);
            if (!channelMatch.Success && type == "RADIO")
                channelMatch = SystemRadioChannel.Match(message);
            if (channelMatch.Success)
            {
                channel = channelMatch.Groups["channel"].Value;
                message = channelMatch.Groups["speaker"].Value + channelMatch.Groups["message"].Value;
                if (type == "MISC")
                    type = "RADIO";
            }
        }

        if (type == "SAY" && message.Contains("announcement", StringComparison.OrdinalIgnoreCase) && Announcement.IsMatch(message))
            type = "ANOUNCE";

        var entry = new LogEntry(timestamp, type, message, lineNumber, channel)
        {
            RawMessage = text
        };
        if (type == "ATTACK")
            return AttackLogEntry.TryCreate(entry) ?? entry;
        if (type == "GAME" && ExplosionLogEntry.TryCreate(entry) is { } explosion)
            return explosion;
        return type is "ADMINPRIVATE" or "ADMIN PRIVATE"
            ? new AdminPrivateLogEntry(entry)
            : PdaLogEntry.TryCreate(entry) ?? SayLogEntry.TryCreate(entry) ?? entry;
    }
}
