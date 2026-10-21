using System;
using System.Collections.Generic;
using System.Linq;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.Filters;

public sealed class TimeLogFilter(DateTime startTime, TimeSpan from, TimeSpan to) : ILogFilter
{
    public IEnumerable<ILogEntry> Apply(IEnumerable<ILogEntry> entries)
    {
        var sorted = entries as ILogEntry[] ?? entries.ToArray();
        var start = LogSearch.FindBoundary(sorted, startTime, from, false);
        var end = LogSearch.FindBoundary(sorted, startTime, to, true);
        for (var index = start; index < end; index++)
            yield return sorted[index];
    }
}
