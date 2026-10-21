using System.Text.RegularExpressions;

namespace Loggepector.Models;

public sealed record ForcedSayLogEntry : SayLogEntry
{
    private readonly string _name;

    internal ForcedSayLogEntry(LogEntry entry, Match speech) : base(entry, speech)
    {
        _name = speech.Groups["name"].Value;
    }

    protected override string GetSpeaker() => $"[FAI]/{_name}";
}
