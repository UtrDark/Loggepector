namespace Loggepector.Models;

public enum LogMessagePartKind
{
    Text,
    CKey,
    CharacterName,
    AI,
    Channel
}

public sealed record LogMessagePart(string Text, LogMessagePartKind Kind, string? Channel = null);
