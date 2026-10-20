using System;

namespace Loggepector.Models;

public sealed record LogEntry(DateTime Timestamp, string Type, string Message, int LineNumber);
