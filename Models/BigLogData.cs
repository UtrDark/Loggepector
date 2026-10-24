using System.Collections.Generic;

namespace Loggepector.Models;

public sealed record BigLogData(IReadOnlyList<ILogEntry> Entries, string? MapName, IReadOnlyList<string> PlayerKeys, string? RoundEndHtml);
