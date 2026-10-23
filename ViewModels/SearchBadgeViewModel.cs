using System.Collections.Generic;
using Loggepector.Models;

namespace Loggepector.ViewModels;

public sealed record SearchBadgeViewModel(LogMessagePart Part)
{
    public IReadOnlyList<LogMessagePart> Parts { get; } = new[] { Part };
}
