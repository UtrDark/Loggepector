using System;

namespace Loggepector.Models;

public interface ILogEntry
{
    DateTime Timestamp { get; }
    string Type { get; }
    int LineNumber { get; }
    string? Channel { get; }
    bool HasCoordinates { get; }
    (int X, int Y, int Z)? GetCoordinates();
    string GetRawMessage();
    string GetLogMessage();
    string GetClearLogMessage();
}
