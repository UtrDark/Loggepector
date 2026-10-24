using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Loggepector.Models;

namespace Loggepector.Utils;

internal static class LogMessageSanitizer
{
    private static readonly Regex Key = new(
        @"^\[[^\]\r\n]+\] (?:[A-Z][A-Z0-9_ ]*: ?)?(?:(?:Mob (?:Login|Logout)|Latejoin): )?(?<key>[^/:\r\n]+?)/\(|(?<key>[A-Za-z0-9_@-]+)/\(|(?:priv_msg=|ckey\s*[:=]\s*|(?:Mob )?(?:Login|Logout):\s*)(?<key>[A-Za-z0-9_@-]+)|^\[[^\]\r\n]+\] MISC: ?(?<key>[A-Za-z0-9_@-]+)\s*\\|(?<![\w:])(?<key>[A-Za-z0-9_@-]+)\s+(?=\(as [^\r\n]*? at -?\d+\s*,\s*-?\d+\s*,\s*-?\d+\))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline);

    private static readonly Regex Address = new(
        @"(?<![\w:])(?:[A-Fa-f0-9]*:){2,}[A-Fa-f0-9:.]*(?:%[\w.-]+)?(?![\w:])|(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])",
        RegexOptions.Compiled);

    internal static Regex? CreateKeyPattern(IEnumerable<ILogEntry> entries)
    {
        var keys = entries.SelectMany(entry => Key.Matches(entry.GetRawMessage()).Cast<Match>())
            .Select(match => match.Groups["key"].Value)
            .SelectMany(key => new[] { key, CKey.Normalize(key) })
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(key => key.Length).Select(Regex.Escape).ToArray();
        return keys.Length == 0 ? null : new Regex(
            @"(?<![\p{L}\p{N}_-])(?:" + string.Join("|", keys) + @")(?![\p{L}\p{N}_-])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static string Clear(string message, Regex? keys)
    {
        message = Address.Replace(message, match => IPAddress.TryParse(match.Value, out _) ? "[IP]" : match.Value);
        return keys is null ? message : keys.Replace(message, "[CKEY]");
    }
}
