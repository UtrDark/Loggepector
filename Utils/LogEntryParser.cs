using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Loggepector.Models;

namespace Loggepector.Utils;

public static class LogEntryParser
{
    private static readonly Regex Header = new(
        @"^\[(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\] (?:(?<type>[A-Z][A-Z0-9_ ]*): ?)?(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex Channel = new(
        @"^(?<speaker>.+? \(mob_\d+\)) \[(?<channel>[^\]\r\n]+)\](?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public static ILogEntry? Parse(string text, int lineNumber)
    {
        var match = Header.Match(text);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["time"].Value,
                "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return null;

        var message = match.Groups["message"].Value;
        var type = match.Groups["type"].Value;
        string? channel = null;
        if (type is "SAY" or "RADIO" or "MISC")
        {
            var channelMatch = Channel.Match(message);
            if (channelMatch.Success)
            {
                channel = channelMatch.Groups["channel"].Value;
                message = channelMatch.Groups["speaker"].Value + channelMatch.Groups["message"].Value;
                if (type == "MISC")
                    type = "RADIO";
            }
        }

        var entry = new LogEntry(timestamp, type, message, lineNumber, channel)
        {
            RawMessage = text
        };
        return type is "ADMINPRIVATE" or "ADMIN PRIVATE"
            ? new AdminPrivateLogEntry(entry)
            : PdaLogEntry.TryCreate(entry) ?? SayLogEntry.TryCreate(entry) ?? entry;
    }
}
