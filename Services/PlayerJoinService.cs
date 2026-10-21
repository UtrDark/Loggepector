using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.Services;

public sealed class PlayerJoinService : IPlayerJoinService
{
    private static readonly Regex Join = new(
        @"^(?:\[[^\]\r\n]+\] )?MISC: ?(?<key>[^\\\r\n]+)\\(?<name>[^\\\r\n]+)\\(?<job>[^\\\r\n]+)\\[^\\\r\n]*\\[ \t]*(?<join>[A-Za-z][A-Za-z0-9_]*)[ \t]*$",
        RegexOptions.Compiled);

    private static readonly Regex Antagonist = new(
        @"^(?:\[[^\]\r\n]+\] )?MISC: ?(?:(?<selection>[^:\r\n]+): )?(?<key>[^/\r\n]+?)/\((?<name>.*?)\) has been selected for (?<role>[^\r\n]+?)\.?[ \t]*$",
        RegexOptions.Compiled);

    public IReadOnlyDictionary<string, IReadOnlyList<PlayerJoin>> PlayerJoins { get; private set; } =
        new Dictionary<string, IReadOnlyList<PlayerJoin>>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, IReadOnlyList<PlayerAntagonist>> PlayerAntagonists { get; private set; } =
        new Dictionary<string, IReadOnlyList<PlayerAntagonist>>(StringComparer.Ordinal);

    public void Process(IEnumerable<ILogEntry> miscEntries)
    {
        var players = new Dictionary<string, List<PlayerJoin>>(StringComparer.Ordinal);
        var antagonists = new Dictionary<string, List<PlayerAntagonist>>(StringComparer.Ordinal);
        foreach (var entry in miscEntries)
        {
            var raw = entry.GetRawMessage();
            var join = Join.Match(raw);
            if (join.Success)
            {
                var key = CKey.Normalize(join.Groups["key"].Value.Trim());
                if (key.Length == 0)
                    continue;

                if (!players.TryGetValue(key, out var joins))
                    players.Add(key, joins = new List<PlayerJoin>());

                joins.Add(new PlayerJoin(join.Groups["job"].Value.Trim(), join.Groups["name"].Value.Trim(),
                    join.Groups["join"].Value));
                continue;
            }

            var antagonist = Antagonist.Match(raw);
            if (!antagonist.Success)
                continue;

            var antagonistKey = CKey.Normalize(antagonist.Groups["key"].Value.Trim());
            if (antagonistKey.Length == 0)
                continue;

            if (!antagonists.TryGetValue(antagonistKey, out var roles))
                antagonists.Add(antagonistKey, roles = new List<PlayerAntagonist>());

            roles.Add(new PlayerAntagonist(antagonist.Groups["name"].Value.Trim(),
                antagonist.Groups["role"].Value.Trim(), antagonist.Groups["selection"].Value.Trim()));
        }

        PlayerJoins = players.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PlayerJoin>)pair.Value.AsReadOnly(),
            StringComparer.Ordinal);
        PlayerAntagonists = antagonists.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<PlayerAntagonist>)pair.Value.AsReadOnly(), StringComparer.Ordinal);
    }
}
