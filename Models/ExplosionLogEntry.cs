using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Loggepector.Models;

public sealed record ExplosionLogEntry : LogEntry
{
    private static readonly Regex Explosion = new(@"^Explosion with size \((?<size>[^)]+)\) in (?<where>.*?)\.\s+Possible cause:\s*(?<cause>.*?)\.\s+Last fingerprints:\s*(?<who>.*?)\.?$",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex Actor = new(@"(?:\*no key\*|[^\s/()]+)/\((?<name>[^)\r\n]+)\)(?: \(mob_\d+\))?", RegexOptions.Compiled);
    private readonly string _displayMessage;
    private readonly Dictionary<string, LogMessagePartKind> _highlights = new();

    public IReadOnlyDictionary<string, LogMessagePartKind> Highlights => _highlights;

    private ExplosionLogEntry(LogEntry entry, Match match) : base(entry with { Type = "EXPLOSION" })
    {
        var who = Actor.Replace(match.Groups["who"].Value.Trim(), "${name}");
        var size = $"size({match.Groups["size"].Value.Trim()})";
        var cause = match.Groups["cause"].Value.Trim();
        _displayMessage = $"Explosion by {who} {size} {cause} {match.Groups["where"].Value.Trim()}";
        if (who.Length > 0)
            _highlights[who] = LogMessagePartKind.CharacterName;
        if (cause.Length > 0)
            _highlights[cause] = LogMessagePartKind.Item;
        _highlights[size] = LogMessagePartKind.DamageType;
    }

    internal static LogEntry? TryCreate(LogEntry entry)
    {
        var match = Explosion.Match(entry.Message);
        return match.Success ? new ExplosionLogEntry(entry, match) : null;
    }

    protected override string FormatMessage() => _displayMessage;
}
