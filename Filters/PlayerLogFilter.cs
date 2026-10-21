using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.Filters;

public sealed class PlayerLogFilter : ILogFilter
{
    private readonly bool _isActive;
    private readonly Regex[] _players;

    public PlayerLogFilter(IReadOnlyList<string> ckeys, IReadOnlyDictionary<string, Regex> players)
    {
        _isActive = ckeys.Count > 0;
        _players = ckeys.Select(CKey.Normalize).Distinct()
            .Select(key => players.TryGetValue(key, out var pattern) ? pattern : new Regex(
                @"(?<![\p{L}\p{N}_-])" + Regex.Escape(key) + @"(?![\p{L}\p{N}_-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
    }

    public IEnumerable<ILogEntry> Apply(IEnumerable<ILogEntry> entries) =>
        _isActive ? entries.Where(entry => _players.Any(player => player.IsMatch(entry.GetRawMessage()))) : entries;
}
