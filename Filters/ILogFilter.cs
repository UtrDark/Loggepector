using System.Collections.Generic;
using Loggepector.Models;

namespace Loggepector.Filters;

public interface ILogFilter
{
    IEnumerable<ILogEntry> Apply(IEnumerable<ILogEntry> entries);
}
