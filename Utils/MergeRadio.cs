using System;
using System.Text.RegularExpressions;
using Loggepector.Models;

namespace Loggepector.Utils;

public static class MergeRadio
{
    private static readonly Regex Speech = new(
        """^(?<speaker>.+? \(mob_\d+\)) (?:\(spans:[^\r\n]*?\) )?"(?<speech>.*)"(?<suffix>.*)$""",
        RegexOptions.Compiled);

    public static ILogEntry? Merge(ILogEntry previous, ILogEntry current)
    {
        if (previous is not SayLogEntry sayEntry || current is not SayLogEntry radioEntry ||
            previous.Type != "SAY" || previous.Channel is not null ||
            current.Type != "RADIO" || current.Channel is null)
            return null;

        var say = Speech.Match(sayEntry.Message);
        var radio = Speech.Match(radioEntry.Message);
        if (!say.Success || !radio.Success ||
            !string.Equals(say.Groups["speaker"].Value, radio.Groups["speaker"].Value, StringComparison.Ordinal) ||
            !string.Equals(say.Groups["speech"].Value, radio.Groups["speech"].Value, StringComparison.OrdinalIgnoreCase))
            return null;

        return sayEntry with
        {
            Channel = current.Channel,
            RawMessage = previous.GetRawMessage() + "\n" + current.GetRawMessage()
        };
    }
}
