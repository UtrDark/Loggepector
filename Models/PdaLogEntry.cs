using System.Text.RegularExpressions;

namespace Loggepector.Models;

public sealed record PdaLogEntry : LogEntry
{
    private static readonly Regex Pda = new(
        """^(?<key>.+?)/\((?<name>.*?)\) \(mob_\d+\)\s*\(\s*PDA: .*? to (?<recipient>.*?)\) "(?<speech>.*)".*$""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly Match _pda;

    private PdaLogEntry(LogEntry entry, Match pda) : base(entry)
    {
        _pda = pda;
    }

    internal static LogEntry? TryCreate(LogEntry entry)
    {
        var pda = Pda.Match(entry.Message);
        return pda.Success ? new PdaLogEntry(entry, pda) : null;
    }

    protected override string FormatMessage() =>
        $"{_pda.Groups["name"].Value}({_pda.Groups["key"].Value}) -> {_pda.Groups["recipient"].Value} \"{_pda.Groups["speech"].Value}\"";
}
