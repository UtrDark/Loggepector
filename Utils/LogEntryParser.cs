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

    public static LogEntry? Parse(string text, int lineNumber)
    {
        var match = Header.Match(text);
        if (!match.Success || !DateTime.TryParseExact(match.Groups["time"].Value,
                "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return null;

        return new LogEntry(timestamp, match.Groups["type"].Value, match.Groups["message"].Value, lineNumber);
    }
}
