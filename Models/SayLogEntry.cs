using System.Text.RegularExpressions;

namespace Loggepector.Models;

public record SayLogEntry : LogEntry
{
    private static readonly Regex Speech = new(
        """^(?<key>.+?)/\((?<name>.*?)\)(?: \(mob_\d+\))?:? (?:\(LOOC\) )?(?:\(spans:[^\r\n]*?\) )?"(?<speech>.*)"(?<suffix>.*)$""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly Match _speech;

    protected SayLogEntry(LogEntry entry, Match speech) : base(entry)
    {
        _speech = speech;
    }

    internal static LogEntry? TryCreate(LogEntry entry)
    {
        var speech = Speech.Match(entry.Message);
        if (!speech.Success)
            return null;

        return speech.Groups["suffix"].Value.Contains("FORCED by AI Controller")
            ? new ForcedSayLogEntry(entry, speech)
            : new SayLogEntry(entry, speech);
    }

    protected virtual string GetSpeaker() => $"{_speech.Groups["name"].Value}({_speech.Groups["key"].Value})";

    protected override string FormatMessage() => $"{GetSpeaker()} \"{_speech.Groups["speech"].Value}\"";
}
