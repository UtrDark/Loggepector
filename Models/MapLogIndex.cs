using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Loggepector.Utils;

namespace Loggepector.Models;

public sealed record MapLogPoint(int X, int Y, int Z, string Name, string PlayerKey, IReadOnlyList<ILogEntry> Entries)
{
    public double Progress { get; init; }
}

public sealed class MapLogIndex
{
    private static readonly Regex Actor = new(@"(?<key>\*no key\*|[^\s/]+)/\((?<name>[^)]+)\)", RegexOptions.Compiled);
    private readonly Dictionary<(int Z, int X, int Y), MapLogPoint[]> _buckets;

    public MapLogIndex(IReadOnlyList<ILogEntry> entries, DateTime start, DateTime end)
    {
        var points = entries.Select(entry => (Entry: entry, Point: entry.GetCoordinates()))
            .Where(item => item.Point.HasValue).GroupBy(item => item.Point!.Value)
            .Select(group =>
            {
                var logs = group.Select(item => item.Entry).OrderBy(entry => entry.Timestamp).ThenBy(entry => entry.LineNumber).ToArray();
                var actors = logs.Select(entry => Actor.Match(entry.GetRawMessage())).Where(match => match.Success).ToArray();
                var names = actors.Select(match => match.Groups["name"].Value).Distinct().ToArray();
                var key = actors.Select(match => match.Groups["key"].Value).Where(value => value != "*no key*")
                    .Select(CKey.Normalize).GroupBy(value => value).OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal).FirstOrDefault()?.Key ?? string.Empty;
                var name = names.Length == 0 ? logs[0].Type : string.Join(", ", names.Take(2));
                if (names.Length > 2)
                    name += $" +{names.Length - 2}";
                return new MapLogPoint(group.Key.X, group.Key.Y, group.Key.Z, name, key, logs);
            }).ToArray();
        if (points.Length > 0)
        {
            var duration = (end - start).TotalSeconds;
            points = points.Select(point => point with
            {
                Progress = duration <= 0 ? 1 : Math.Clamp((point.Entries[^1].Timestamp - start).TotalSeconds / duration, 0, 1)
            }).ToArray();
        }
        _buckets = points.GroupBy(point => (point.Z, point.X / 16, point.Y / 16))
            .ToDictionary(group => group.Key, group => group.ToArray());
    }

    public MapLogPoint[] Query(int z, double left, double bottom, double right, double top)
    {
        var points = new List<MapLogPoint>();
        for (var x = (int)Math.Floor(left / 16); x <= (int)Math.Floor(right / 16); x++)
            for (var y = (int)Math.Floor(bottom / 16); y <= (int)Math.Floor(top / 16); y++)
                if (_buckets.TryGetValue((z, x, y), out var bucket))
                    points.AddRange(bucket.Where(point => point.X >= left && point.X <= right && point.Y >= bottom && point.Y <= top));
        var centerX = (left + right) / 2;
        var centerY = (bottom + top) / 2;
        return points.OrderBy(point => Math.Pow(point.X - centerX, 2) + Math.Pow(point.Y - centerY, 2))
            .ThenBy(point => point.X).ThenBy(point => point.Y).Take(200).ToArray();
    }
}
