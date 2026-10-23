using System.Globalization;
using System.Text.RegularExpressions;

namespace Loggepector.Models;

public sealed record LogisLogFile(string Folder)
{
    private static readonly Regex Round = new(@"(?:^|/)round-(?<number>\d+)(?:/|$)", RegexOptions.Compiled);
    public int? RoundNumber { get; } = GetRoundNumber(Folder);

    private static int? GetRoundNumber(string folder)
    {
        var match = Round.Match(folder);
        return match.Success && int.TryParse(match.Groups["number"].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    public string DisplayName => $"{Folder.TrimStart('/')}/game.log";
}
