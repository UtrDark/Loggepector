using System;
using System.Collections.Generic;
using System.Linq;
using Loggepector.Models;

namespace Loggepector.Utils;

public static class LogNeighbors
{
    public static IReadOnlyList<ILogEntry> Find(IReadOnlyList<ILogEntry> entries, IReadOnlyList<ILogEntry> matched,
        bool sameType, bool byCoordinates, int count, double radius)
    {
        var result = matched.ToDictionary(entry => entry.LineNumber);
        if (sameType)
        {
            foreach (var group in matched.GroupBy(entry => entry.Type))
            {
                var candidates = entries.Where(entry => entry.Type == group.Key).ToArray();
                var indices = candidates.Select((entry, index) => (entry.LineNumber, Index: index))
                    .ToDictionary(entry => entry.LineNumber, entry => entry.Index);
                foreach (var entry in group)
                {
                    if (!indices.TryGetValue(entry.LineNumber, out var index))
                        continue;
                    for (var offset = Math.Max(0, index - count); offset <= Math.Min(candidates.Length - 1, index + count); offset++)
                        result[candidates[offset].LineNumber] = candidates[offset];
                }
            }
        }
        if (byCoordinates)
        {
            var centers = matched.Select(entry => entry.GetCoordinates()).Where(point => point.HasValue)
                .Select(point => point!.Value).Distinct().ToArray();
            var cellSize = Math.Max(1, radius);
            var cells = centers.GroupBy(point => (point.Z,
                    X: (int)Math.Floor(point.X / cellSize), Y: (int)Math.Floor(point.Y / cellSize)))
                .ToDictionary(group => group.Key, group => group.ToArray());
            var squaredRadius = radius * radius;
            foreach (var entry in entries)
            {
                if (result.ContainsKey(entry.LineNumber) || entry.GetCoordinates() is not { } point)
                    continue;
                var cellX = (int)Math.Floor(point.X / cellSize);
                var cellY = (int)Math.Floor(point.Y / cellSize);
                var found = false;
                for (var x = cellX - 1; x <= cellX + 1 && !found; x++)
                    for (var y = cellY - 1; y <= cellY + 1 && !found; y++)
                        if (cells.TryGetValue((point.Z, x, y), out var nearby))
                            found = nearby.Any(center =>
                                Math.Pow((double)point.X - center.X, 2) + Math.Pow((double)point.Y - center.Y, 2) <= squaredRadius);
                if (found)
                    result[entry.LineNumber] = entry;
            }
        }
        return result.Values.OrderBy(entry => entry.Timestamp).ThenBy(entry => entry.LineNumber).ToArray();
    }
}
