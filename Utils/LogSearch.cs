using System;
using Loggepector.Models;

namespace Loggepector.Utils;

public static class LogSearch
{
    public static int FindBoundary(ILogEntry[] entries, DateTime startTime, TimeSpan offset, bool upper)
    {
        var low = 0;
        var high = entries.Length;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            var time = entries[middle].Timestamp - startTime;
            if (time < offset || (upper && time == offset))
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}
