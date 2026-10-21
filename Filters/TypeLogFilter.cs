using System;
using System.Collections.Generic;
using System.Linq;
using Loggepector.Models;

namespace Loggepector.Filters;

public sealed class TypeLogFilter(IEnumerable<string> types) : ILogFilter
{
    private readonly HashSet<string> _types = new(types, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<ILogEntry> Apply(IEnumerable<ILogEntry> entries) =>
        _types.Count == 0 ? entries : entries.Where(entry => _types.Contains(entry.Type));
}
