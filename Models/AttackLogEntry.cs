using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Loggepector.Models;

public sealed record AttackLogEntry : LogEntry
{
    private static readonly Regex Attack = new(@"^(?<actor>.*?) (?<action>attacked|shot|injected|attempted to inject) (?<target>.*?)(?: with (?<item>.*?))?(?= \((?:COMBAT MODE|DAMTYPE|NEWHP):|$)(?<details>.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Grab = new(@"^(?<actor>.*?) grabbed (?<target>.*?) (?<item>passive grab|aggressive grab|neck grab|kill grab)(?<details>.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Death = new(@"^(?<actor>.*?) has died \(BRUTE: (?<brute>[^,]+), BURN: (?<burn>[^,]+), TOX: (?<tox>[^,]+), OXY: (?<oxy>[-+\d.]+)(?<details>.*)$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CombatMode = new(@"\(COMBAT MODE: (?<mode>[01])\)", RegexOptions.Compiled);
    private static readonly Regex Mob = new(@"\s*\(mob_\d+\)", RegexOptions.Compiled);
    private static readonly Regex Location = new(@"\s*\([^()]+?\(\s*-?\d+\s*,\s*-?\d+\s*,\s*-?\d+\s*\)\s*\)", RegexOptions.Compiled);
    private static readonly Regex Health = new(@"\(NEWHP:\s*(?<value>[^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex DamageType = new(@"\(DAMTYPE:\s*(?<value>[^)]+)\)", RegexOptions.Compiled);
    private static readonly Regex UsedItem = new(@"^(?<actor>.*?) has (?<action>eaten|thrown) (?:the )?(?<item>.*?)(?<details>, (?:destroying|swallowing).*$|$)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Transfer = new(@"^(?<actor>.*?) (?<action>transferred reagents) to (?<target>.*?) with (?:the )?(?<item>.*?)(?<details> which had.*|$)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex FiredAt = new(@"^(?<actor>.*?) (?<action>fired at) (?<target>.*?) with (?:the )?(?<item>.*?)(?: from (?<area>.*?))?(?<details> \((?:COMBAT MODE|DAMTYPE|NEWHP):.*|$)$", RegexOptions.Compiled | RegexOptions.Singleline);
    private readonly string _displayMessage;
    private readonly Dictionary<string, LogMessagePartKind> _highlights = new();

    public IReadOnlyDictionary<string, LogMessagePartKind> Highlights => _highlights;

    private AttackLogEntry(LogEntry entry, string message) : base(entry)
    {
        _highlights["died"] = LogMessagePartKind.Action;
        _displayMessage = NormalizeDetails(message);
    }

    private string NormalizeDetails(string message)
    {
        message = Health.Replace(message, match =>
        {
            var label = $"HP: {match.Groups["value"].Value.Trim()}";
            _highlights[label] = LogMessagePartKind.Health;
            return label;
        });
        return DamageType.Replace(message, match =>
        {
            var label = match.Groups["value"].Value.Trim();
            _highlights[label] = LogMessagePartKind.DamageType;
            return label;
        });
    }

    private AttackLogEntry(LogEntry entry, Match match, bool death, bool grab) : base(entry)
    {
        var actor = match.Groups["actor"].Value;
        if (!actor.Contains("/("))
            _highlights[actor] = LogMessagePartKind.CharacterName;
        if (death)
        {
            _highlights["died"] = LogMessagePartKind.Action;
            var damage = new List<string>();
            foreach (var (name, kind) in new[] { ("brute", LogMessagePartKind.Brute), ("burn", LogMessagePartKind.Burn),
                         ("tox", LogMessagePartKind.Tox), ("oxy", LogMessagePartKind.Oxy) })
            {
                var label = $"{name}: {match.Groups[name].Value.Trim()}";
                _highlights[label] = kind;
                damage.Add(label);
            }
            _displayMessage = NormalizeDetails($"{actor} died {string.Join(" ", damage)} {match.Groups["details"].Value.TrimStart(')', ' ')}".TrimEnd());
            return;
        }
        if (match.Groups["action"].Value is "eaten" or "thrown")
        {
            var verb = match.Groups["action"].Value;
            var usedItem = match.Groups["item"].Value;
            _highlights[verb] = LogMessagePartKind.Action;
            _highlights[usedItem] = LogMessagePartKind.Item;
            _displayMessage = NormalizeDetails($"{actor} {verb} {usedItem}{match.Groups["details"].Value}");
            return;
        }
        var action = grab ? "grab" : match.Groups["action"].Value switch
        {
            "attacked" => "Attack",
            "shot" => "shot",
            "fired at" => "shot",
            "transferred reagents" => "transfer",
            "attempted to inject" => "attempt inject",
            _ => "inject"
        };
        var target = match.Groups["target"].Value;
        var item = match.Groups["item"].Value;
        _highlights[action] = LogMessagePartKind.Action;
        if (!target.Contains("/("))
            _highlights[target] = LogMessagePartKind.CharacterName;
        if (item.Length > 0)
            _highlights[item] = LogMessagePartKind.Item;
        var area = match.Groups["area"].Value;
        if (area.Length > 0)
            _highlights[area] = LogMessagePartKind.Area;
        var details = CombatMode.Replace(match.Groups["details"].Value, mode =>
        {
            var label = mode.Groups["mode"].Value == "0" ? "help" : "harm";
            _highlights[label] = label == "help" ? LogMessagePartKind.Help : LogMessagePartKind.Harm;
            return label;
        });
        _displayMessage = NormalizeDetails($"{actor} {action} {target}{(item.Length > 0 ? $" by {item}" : string.Empty)}{details}{(area.Length > 0 ? $" from {area}" : string.Empty)}");
    }

    internal static LogEntry? TryCreate(LogEntry entry)
    {
        var message = Location.Replace(Mob.Replace(entry.Message, string.Empty), string.Empty);
        var firedAt = FiredAt.Match(message);
        if (firedAt.Success)
            return new AttackLogEntry(entry, firedAt, false, false);
        if (message.Contains(" fired at "))
            return new AttackLogEntry(entry, message);
        var usedItem = UsedItem.Match(message);
        if (usedItem.Success)
            return new AttackLogEntry(entry, usedItem, false, false);
        var transfer = Transfer.Match(message);
        if (transfer.Success)
            return new AttackLogEntry(entry, transfer, false, false);
        var death = Death.Match(message);
        if (death.Success)
            return new AttackLogEntry(entry, death, true, false);
        var grab = Grab.Match(message);
        if (grab.Success)
            return new AttackLogEntry(entry, grab, false, true);
        var attack = Attack.Match(message);
        return attack.Success ? new AttackLogEntry(entry, attack, false, false) : new AttackLogEntry(entry, message);
    }

    protected override string FormatMessage() => _displayMessage;
}
