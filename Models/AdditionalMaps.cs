using System.Text.RegularExpressions;

namespace Loggepector.Models;

internal static class AdditionalMaps
{
    private static readonly Regex LavalandArea = new(@"\(\s*Lavaland\b[^()]*\(\s*\d+\s*,\s*\d+\s*,\s*\d+\s*\)\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsLavaland(ILogEntry entry) => LavalandArea.IsMatch(entry.GetRawMessage());

    public static bool IsPlanetaryPort(ILogEntry entry) => entry.GetCoordinates() is { Z: 1 };

    public const string PlanetaryPortName = "Планетарный порт";
    public const string PlanetaryPortPath = "_maps/map_files/generic/CentCom.dmm";
    public const string PlanetaryPortConfig = "{\"map_name\":\"Планетарный порт\",\"map_path\":\"map_files/generic\",\"map_file\":\"CentCom.dmm\",\"main_floor\":1}";

    public const string LavalandName = "Lavaland";
    public const string LavalandPath = "_maps/map_files/Mining/Lavaland.dmm";
    public const string LavalandConfig = "{\"map_name\":\"Lavaland\",\"map_path\":\"map_files/Mining\",\"map_file\":\"Lavaland.dmm\",\"main_floor\":1}";
}
