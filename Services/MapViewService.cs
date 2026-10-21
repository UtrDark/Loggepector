using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.Services;

public sealed class MapViewService : IMapViewService
{
    public async Task<MapDocument> LoadAsync(string mapName, CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "MapCache");
        var statePath = Path.Combine(root, "state.json");
        if (!File.Exists(statePath))
            throw new InvalidOperationException("Карты ещё не подготовлены. Включите загрузку инструментов карт.");
        var state = await MapCacheFiles.ReadAsync<MapCacheState>(statePath, cancellationToken);
        foreach (var configPath in state.SourceFiles.Keys.Where(path => path.StartsWith("_maps/map_jsons/", StringComparison.Ordinal)))
        {
            var path = MapCacheFiles.Resolve(Path.Combine(root, "source"), configPath);
            await using var stream = File.OpenRead(path);
            using var config = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
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
                    throw new InvalidOperationException($"Карта {name} ещё не отрендерена. Дождитесь подготовки и нажмите «Обновить».");
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
            return new MapDocument(name, 32, 1024, layer, tiles.ToArray());
        }
        throw new InvalidOperationException($"Карта «{mapName}» не найдена в конфигурациях BandaStation.");
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
