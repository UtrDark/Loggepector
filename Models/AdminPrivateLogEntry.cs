using System;
using System.Net;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Loggepector.Utils;

namespace Loggepector.Models;

public sealed record AdminPrivateLogEntry : LogEntry
{
    private static readonly Regex HtmlLineBreak = new(
        @"<br\s*/?>|</(?:p|div|li|tr)\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTag = new(
        """</?[A-Za-z](?:[^>"']|"[^"]*"|'[^']*')*>""", RegexOptions.Compiled);

    private static readonly Regex PlayerLink = new(
        """<a\b[^>]*\bhref\s*=\s*["']byond://\?[^"']*\bpriv_msg=[^"']*["'][^>]*>(?<key>.*?)</a\s*>""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex Ticket = new(@"\b(?:Ticket|Тикет)\s*#(?<number>\d+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal IReadOnlyDictionary<string, LogMessagePartKind> Highlights { get; }

    public AdminPrivateLogEntry(LogEntry entry) : base(entry)
    {
        var highlights = new Dictionary<string, LogMessagePartKind>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in PlayerLink.Matches(Message))
        {
            var key = WebUtility.HtmlDecode(HtmlTag.Replace(match.Groups["key"].Value, string.Empty)).Trim();
            if (key.Length > 0)
                highlights[key] = LogMessagePartKind.CKey;
        }
        foreach (Match match in Ticket.Matches(FormatMessage()))
            highlights[match.Value] = LogMessagePartKind.Ticket;
        Highlights = highlights;
    }

    protected override string FormatMessage() =>
        Ticket.Replace(WebUtility.HtmlDecode(HtmlTag.Replace(HtmlLineBreak.Replace(EmojiMarkup.Replace(Message), "\n"), string.Empty)), "Ticket #${number}");
}
