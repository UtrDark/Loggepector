namespace Loggepector.Models;

internal static class AdditionalMaps
{
    public static bool IsLavaland(ILogEntry entry, int? worldZ) =>
        worldZ.HasValue && entry.GetCoordinates() is { } coordinates && coordinates.Z == worldZ.Value;

    public static bool IsPlanetaryPort(ILogEntry entry) => entry.GetCoordinates() is { Z: 1 };

    public const string PlanetaryPortName = "CentCom";
    public const string PlanetaryPortPath = "_maps/map_files/generic/CentCom.dmm";
    public const string PlanetaryPortConfig = "{\"map_name\":\"CentCom\",\"map_path\":\"map_files/generic\",\"map_file\":\"CentCom.dmm\",\"main_floor\":1}";

    public const string LavalandName = "Lavaland";
    public const string LavalandPath = "_maps/map_files/Mining/Lavaland.dmm";
    public const string LavalandConfig = "{\"map_name\":\"Lavaland\",\"map_path\":\"map_files/Mining\",\"map_file\":\"Lavaland.dmm\",\"main_floor\":1}";
}
