using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.Filters;

public sealed class TextLogFilter : ILogFilter
{
    private readonly bool _fuzzy;
    private readonly string[] _terms;
    private readonly Regex[] _exactTerms;
    private readonly LogMessagePart[][] _badgeGroups;

    public TextLogFilter(string text, bool fuzzy = false, IReadOnlyList<LogMessagePart>? badges = null)
    {
        _fuzzy = fuzzy;
        _terms = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        _exactTerms = fuzzy ? Array.Empty<Regex>() : _terms.Select(term => new Regex(
            @"(?<![\p{L}\p{N}_])" + Regex.Escape(term) + @"(?![\p{L}\p{N}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        _badgeGroups = (badges ?? Array.Empty<LogMessagePart>()).GroupBy(part => part.Kind)
            .Select(group => group.ToArray()).ToArray();
    }

    public IEnumerable<ILogEntry> Apply(IEnumerable<ILogEntry> entries) =>
        _terms.Length == 0 && _badgeGroups.Length == 0 ? entries : entries.Where(Matches);

    private bool Matches(ILogEntry entry)
    {
        IReadOnlyList<LogMessagePart>? actual = null;
        IReadOnlyList<LogMessagePart> GetBadges() => actual ??= entry is LogEntry log ? log.GetSearchBadges()
            : LogMessageHighlights.Create(entry, entry.GetLogMessage(), true);
        if (_terms.Length > 0)
        {
            var raw = entry.GetRawMessage();
            var display = entry.GetLogMessage();
            if (_fuzzy
                ? !_terms.All(term => raw.Contains(term, StringComparison.OrdinalIgnoreCase) || display.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    GetBadges().Any(part => part.Kind != LogMessagePartKind.Text && part.Text.Contains(term, StringComparison.OrdinalIgnoreCase)))
                : !_exactTerms.All(term => term.IsMatch(raw) || term.IsMatch(display) ||
                    GetBadges().Any(part => part.Kind != LogMessagePartKind.Text && term.IsMatch(part.Text))))
                return false;
        }
        if (_badgeGroups.Length == 0)
            return true;
        return _badgeGroups.All(group => group.Any(wanted => GetBadges().Any(part => part.Kind == wanted.Kind &&
            (_fuzzy ? part.Text.Contains(wanted.Text, StringComparison.OrdinalIgnoreCase)
                : part.Text.Equals(wanted.Text, StringComparison.OrdinalIgnoreCase)))));
    }
}
