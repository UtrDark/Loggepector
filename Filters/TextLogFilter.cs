using System;
using System.Collections.Generic;
using System.Linq;
using Loggepector.Models;

namespace Loggepector.Filters;

public sealed class TextLogFilter(string text) : ILogFilter
{
    public IEnumerable<ILogEntry> Apply(IEnumerable<ILogEntry> entries) =>
        string.IsNullOrEmpty(text) ? entries :
            entries.Where(entry => entry.GetRawMessage().Contains(text, StringComparison.OrdinalIgnoreCase));
}
