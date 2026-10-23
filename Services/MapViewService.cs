using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.Services;

public sealed class MapViewService : IMapViewService
{
    public async Task<int> GetStationLayerCountAsync(string mapName, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "MapCache");
        var state = await MapCacheFiles.ReadAsync<MapCacheState>(Path.Combine(root, "state.json"), cancellationToken);
        var source = Path.Combine(root, "source");
        foreach (var configPath in state.SourceFiles.Keys.Where(path => path.StartsWith("_maps/map_jsons/", StringComparison.Ordinal)))
        {
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(MapCacheFiles.Resolve(source, configPath), cancellationToken));
            if (Normalize(config.RootElement.GetProperty("map_name").GetString()!) != Normalize(mapName) &&
                Normalize(Path.GetFileNameWithoutExtension(configPath)) != Normalize(mapName))
                continue;
            var directory = config.RootElement.GetProperty("map_path").GetString()!;
            var mapFile = config.RootElement.GetProperty("map_file");
            var files = mapFile.ValueKind == JsonValueKind.Array
                ? mapFile.EnumerateArray().Select(file => file.GetString()!).ToArray() : new[] { mapFile.GetString()! };
            var count = 0;
            foreach (var file in files)
            {
                var text = await File.ReadAllTextAsync(MapCacheFiles.Resolve(source, $"_maps/{directory}/{file}".Replace('\\', '/')), cancellationToken);
                var levels = Regex.Matches(text, @"^\([ \t]*[0-9]+[ \t]*,[ \t]*[0-9]+[ \t]*,[ \t]*(?<z>[0-9]+)[ \t]*\)",
                    RegexOptions.Multiline).Cast<Match>().Select(match => int.Parse(match.Groups["z"].Value)).Distinct().ToArray();
                if (levels.Length == 0)
                    throw new InvalidDataException($"Не удалось определить Z-уровни станции: {file}.");
                count += levels.Length;
            }
            return count;
        }
        throw new InvalidOperationException($"Карта станции «{mapName}» не найдена в конфигурациях репозитория.");
    }

    public async Task<MapDocument> LoadAsync(string mapName, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "MapCache");
        var statePath = Path.Combine(root, "state.json");
        if (!File.Exists(statePath))
            throw new InvalidOperationException("Карты ещё не подготовлены. Включите загрузку инструментов карт.");
        var state = await MapCacheFiles.ReadAsync<MapCacheState>(statePath, cancellationToken);
        foreach (var configPath in state.SourceFiles.Keys.Where(path => path.StartsWith("_maps/map_jsons/", StringComparison.Ordinal))
                     .Append(AdditionalMaps.LavalandPath).Append(AdditionalMaps.PlanetaryPortPath))
        {
            var path = MapCacheFiles.Resolve(Path.Combine(root, "source"), configPath);
            using var config = JsonDocument.Parse(configPath == AdditionalMaps.LavalandPath
                ? AdditionalMaps.LavalandConfig
                : configPath == AdditionalMaps.PlanetaryPortPath ? AdditionalMaps.PlanetaryPortConfig
                : await File.ReadAllTextAsync(path, cancellationToken));
            var name = config.RootElement.GetProperty("map_name").GetString()!;
            if (Normalize(name) != Normalize(mapName) &&
                Normalize(Path.GetFileNameWithoutExtension(configPath)) != Normalize(mapName))
                continue;

            var directory = config.RootElement.GetProperty("map_path").GetString()!;
            var mapFile = config.RootElement.GetProperty("map_file");
            var files = mapFile.ValueKind == JsonValueKind.Array
                ? mapFile.EnumerateArray().Select(file => file.GetString()!).ToArray() : new[] { mapFile.GetString()! };
            var tiles = new List<MapViewTile>();
            var zOffset = 0;
            foreach (var file in files)
            {
                var source = $"_maps/{directory}/{file}".Replace('\\', '/');
                if (!state.Maps.TryGetValue(source, out var ready))
                    throw new InvalidOperationException($"Карта {name} ещё не отрендерена. Выберите её заново.");
                var rendered = MapCacheFiles.Resolve(root, ready.Directory);
                var manifest = await MapCacheFiles.ReadAsync<MapTileManifest>(Path.Combine(rendered, "manifest.json"), cancellationToken);
                if (manifest.Chunks.Length == 0 || manifest.ChunkSize != 1024 || manifest.TileSize != 32)
                    throw new InvalidDataException($"Некорректный манифест карты {source}.");
                tiles.AddRange(manifest.Chunks.Select(chunk => new MapViewTile(
                    MapCacheFiles.Resolve(rendered, chunk.File), chunk with { Z = chunk.Z + zOffset })));
                zOffset += manifest.Chunks.Max(chunk => chunk.Z);
            }
            var layer = config.RootElement.TryGetProperty("main_floor", out var floor) ? floor.GetInt32() : 1;
            if (!tiles.Any(tile => tile.Chunk.Z == layer))
                throw new InvalidDataException($"Начальный слой Z={layer} отсутствует в рендере {name}.");
            if (configPath == AdditionalMaps.PlanetaryPortPath)
                return new MapDocument(name, 32, 1024, layer, tiles.ToArray(), 0);
            var centcom = await File.ReadAllTextAsync(Path.Combine(root, "source", "_maps", "map_files", "generic", "CentCom.dmm"),
                cancellationToken);
            var levels = Regex.Matches(centcom, @"^\([ \t]*[0-9]+[ \t]*,[ \t]*[0-9]+[ \t]*,[ \t]*(?<z>[0-9]+)[ \t]*\)",
                RegexOptions.Multiline).Cast<Match>().Select(match => int.Parse(match.Groups["z"].Value)).ToArray();
            if (levels.Length == 0)
                throw new InvalidDataException("Не удалось определить смещение Z-уровней станции по CentCom.dmm.");
            return new MapDocument(name, 32, 1024, layer, tiles.ToArray(), levels.Max());
        }
        throw new InvalidOperationException($"Карта «{mapName}» не найдена в конфигурациях репозитория.");
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
