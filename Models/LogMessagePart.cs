namespace Loggepector.Models;

public enum LogMessagePartKind
{
    Text,
    CKey,
    CharacterName,
    AI,
    Channel,
    Action,
    Item,
    Help,
    Harm,
    Brute,
    Burn,
    Tox,
    Oxy,
    Health,
    DamageType,
    Area,
    Ticket,
    Emoji
}

public sealed record LogMessagePart(string Text, LogMessagePartKind Kind, string? Channel = null, bool IsExcluded = false);

public sealed record BadgeSearchRequest(LogMessagePart Part, bool Add);
