using System.Net;
using System.Text.RegularExpressions;

namespace Loggepector.Models;

public sealed record AdminPrivateLogEntry : LogEntry
{
    private static readonly Regex HtmlLineBreak = new(
        @"<br\s*/?>|</(?:p|div|li|tr)\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HtmlTag = new(
        """</?[A-Za-z](?:[^>"']|"[^"]*"|'[^']*')*>""", RegexOptions.Compiled);

    public AdminPrivateLogEntry(LogEntry entry) : base(entry)
    {
    }

    protected override string FormatMessage() =>
        WebUtility.HtmlDecode(HtmlTag.Replace(HtmlLineBreak.Replace(Message, "\n"), string.Empty));
}
