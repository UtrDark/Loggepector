using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.Services;

public sealed class MapPreparationService : IMapPreparationService
{
    private const string Repository = "ss220club/BandaStation";
    private const int SourceSchemaVersion = 3;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".gif", ".jpg", ".jpeg", ".bmp", ".webp"
    };
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromHours(1) };
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "MapCache");
    private readonly string _settings = Path.Combine(AppContext.BaseDirectory, "map-tools-settings.json");

    public MapPreparationService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Loggepector/1.0");
    }

    public async Task<bool?> GetChoiceAsync(CancellationToken cancellationToken = default) =>
        File.Exists(_settings)
            ? (await MapCacheFiles.ReadAsync<MapToolsSettings>(_settings, cancellationToken)).Enabled : null;

    public Task SaveChoiceAsync(bool enabled, CancellationToken cancellationToken = default) =>
        MapCacheFiles.SaveAsync(_settings, new MapToolsSettings(enabled), cancellationToken);

    public async Task PrepareAsync(IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        await using var cacheLock = new FileStream(Path.Combine(_root, "update.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        var statePath = Path.Combine(_root, "state.json");
        var state = File.Exists(statePath)
            ? await MapCacheFiles.ReadAsync<MapCacheState>(statePath, cancellationToken) : new MapCacheState();
        var renderer = await EnsureRendererAsync(progress, cancellationToken);
        progress.Report("Проверка текущего коммита BandaStation…");
        using var repository = await GetJsonAsync($"https://api.github.com/repos/{Repository}", cancellationToken);
        var branch = repository.RootElement.GetProperty("default_branch").GetString()!;
        using var head = await GetJsonAsync($"https://api.github.com/repos/{Repository}/commits/{Uri.EscapeDataString(branch)}",
            cancellationToken);
        var commit = head.RootElement.GetProperty("sha").GetString()!;
        var source = Path.Combine(_root, "source");
        var files = state.SourceFiles;
        if (state.SourceSchemaVersion != SourceSchemaVersion || commit != state.SourceCommit || files.Count == 0 || files.Keys.Any(file => !IsRenderSource(file)) ||
            files.Keys.Any(file => !File.Exists(MapCacheFiles.Resolve(source, file))))
        {
            progress.Report("Сравнение файлов карт, кода и иконок…");
            using var tree = await GetJsonAsync($"https://api.github.com/repos/{Repository}/git/trees/{commit}?recursive=1",
                cancellationToken);
            if (tree.RootElement.GetProperty("truncated").GetBoolean())
                throw new InvalidDataException("GitHub вернул неполный список файлов BandaStation.");
            files = tree.RootElement.GetProperty("tree").EnumerateArray()
                .Where(item => item.GetProperty("type").GetString() == "blob")
                .Select(item => (Path: item.GetProperty("path").GetString()!, Sha: item.GetProperty("sha").GetString()!))
                .Where(item => IsRenderSource(item.Path)).ToDictionary(item => item.Path, item => item.Sha);
            if (!files.ContainsKey("tgstation.dme"))
                throw new InvalidDataException("В BandaStation отсутствует tgstation.dme.");
            Directory.CreateDirectory(source);
            var changed = files.Where(file => !state.SourceFiles.TryGetValue(file.Key, out var sha) ||
                sha != file.Value || !File.Exists(MapCacheFiles.Resolve(source, file.Key))).ToArray();
            var completed = 0;
            progress.Report($"Проверка и загрузка {changed.Length} необходимых файлов рендера…");
            await Parallel.ForEachAsync(changed, new ParallelOptions
            {
                MaxDegreeOfParallelism = 6,
                CancellationToken = cancellationToken
            }, async (file, token) =>
            {
                var destination = MapCacheFiles.Resolve(source, file.Key);
                if (!File.Exists(destination) || !string.Equals(await GetBlobShaAsync(destination, token),
                        file.Value, StringComparison.OrdinalIgnoreCase))
                {
                    var url = $"https://raw.githubusercontent.com/{Repository}/{commit}/" +
                        string.Join("/", file.Key.Split('/').Select(Uri.EscapeDataString));
                    var candidate = destination + ".candidate";
                    await DownloadAsync(url, candidate, token);
                    await VerifyBlobAsync(candidate, file.Value, token);
                    File.Move(candidate, destination, true);
                }
                var count = Interlocked.Increment(ref completed);
                if (count % 25 == 0 || count == changed.Length)
                    progress.Report($"Файлы рендера: {count}/{changed.Length}");
            });
            foreach (var removed in state.SourceFiles.Keys.Except(files.Keys))
                File.Delete(MapCacheFiles.Resolve(source, removed));
            state.SourceFiles = files;
            state.SourceCommit = commit;
            state.SourceSchemaVersion = SourceSchemaVersion;
            await MapCacheFiles.SaveAsync(statePath, state, cancellationToken);
        }

        var stationMaps = await GetStationMapsAsync(source, cancellationToken);
        foreach (var path in stationMaps)
            if (!files.ContainsKey(path))
                throw new InvalidDataException($"Основная карта из конфигурации не найдена: {path}");
        var dependencies = string.Join("\n", files.Where(file => !IsMap(file.Key) && !IsMapSelectionFile(file.Key))
            .OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => file.Key + ":" + file.Value));
        await using (var binary = File.OpenRead(renderer))
            dependencies += Convert.ToHexString(await SHA256.HashDataAsync(binary, cancellationToken));
        var dependencyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dependencies)));
        var maps = files.Where(file => stationMaps.Contains(file.Key)).OrderBy(file => file.Key, StringComparer.Ordinal).ToArray();
        if (maps.Length == 0)
            throw new InvalidDataException("В конфигурации ротации не найдены основные карты.");
        for (var index = 0; index < maps.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var map = maps[index];
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                map.Key + map.Value + dependencyHash + ":chunks-1024-v1")));
            if (state.Maps.TryGetValue(map.Key, out var ready) && ready.Fingerprint == fingerprint &&
                ready.Files.Length > 0 && ready.Files.All(file => File.Exists(
                    MapCacheFiles.Resolve(MapCacheFiles.Resolve(_root, ready.Directory), file))))
                continue;
            progress.Report($"Рендер карты {index + 1}/{maps.Length}: {map.Key}");
            var relative = "rendered/" + fingerprint + "-" + Guid.NewGuid().ToString("N");
            var output = MapCacheFiles.Resolve(_root, relative);
            var rendered = await MapRenderer.RenderAsync(renderer, source, map.Key, output, commit, progress, cancellationToken);
            state.Maps[map.Key] = new MapRenderState(fingerprint, relative, rendered);
            await MapCacheFiles.SaveAsync(statePath, state, cancellationToken);
        }
        foreach (var removed in state.Maps.Keys.Where(path => !stationMaps.Contains(path)).ToArray())
            state.Maps.Remove(removed);
        await MapCacheFiles.SaveAsync(statePath, state, cancellationToken);
        progress.Report($"Карты готовы: {maps.Length}. BandaStation {commit[..8]}");
    }

    private async Task<string> EnsureRendererAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitOperatingSystem)
            throw new PlatformNotSupportedException("Автозагрузка рендера поддерживается на Windows x64.");
        var renderer = Path.Combine(_root, "tools", "dmm-tools.exe");
        if (File.Exists(renderer))
            return renderer;
        progress.Report("Загрузка dmm-tools из официального релиза SpacemanDMM…");
        using var release = await GetJsonAsync("https://api.github.com/repos/SpaceManiac/SpacemanDMM/releases/latest",
            cancellationToken);
        var asset = release.RootElement.GetProperty("assets").EnumerateArray().Single(item =>
            string.Equals(item.GetProperty("name").GetString(), "dmm-tools.exe", StringComparison.OrdinalIgnoreCase));
        var candidate = renderer + ".candidate";
        await DownloadAsync(asset.GetProperty("browser_download_url").GetString()!, candidate, cancellationToken);
        var expected = asset.GetProperty("digest").GetString()
            ?? throw new InvalidDataException("GitHub не предоставил контрольную сумму dmm-tools.");
        if (!expected.StartsWith("sha256:", StringComparison.Ordinal))
            throw new InvalidDataException("Неизвестный формат контрольной суммы dmm-tools.");
        await using (var stream = File.OpenRead(candidate))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!string.Equals(actual, expected[7..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA256 скачанного dmm-tools не совпадает с релизом.");
        }
        File.Move(candidate, renderer, true);
        return renderer;
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var temporary = destination + ".download";
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(temporary))
            await input.CopyToAsync(output, cancellationToken);
        File.Move(temporary, destination, true);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task<string> GetBlobShaAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.UTF8.GetBytes($"blob {stream.Length}\0"));
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) != 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task VerifyBlobAsync(string path, string expected, CancellationToken cancellationToken)
    {
        if (!string.Equals(await GetBlobShaAsync(path, cancellationToken), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Контрольная сумма файла не совпадает с GitHub: {path}");
    }

    private static bool IsMap(string path) => path.StartsWith("_maps/map_files/", StringComparison.Ordinal) &&
        path.EndsWith(".dmm", StringComparison.OrdinalIgnoreCase);

    private static async Task<HashSet<string>> GetStationMapsAsync(string source, CancellationToken cancellationToken)
    {
        var rotation = await File.ReadAllLinesAsync(Path.Combine(source, "config", "maps.txt"), cancellationToken);
        var enabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? name = null;
        var votable = false;
        var disabled = false;
        foreach (var line in rotation)
        {
            var text = line.Split('#', 2)[0].Trim();
            if (text.StartsWith("map ", StringComparison.Ordinal))
            {
                name = text[4..].Trim();
                votable = false;
                disabled = false;
            }
            else if (text == "votable")
                votable = true;
            else if (text == "disabled")
                disabled = true;
            else if (text == "endmap" && name is not null)
            {
                enabled[name] = votable && !disabled;
                name = null;
            }
        }

        var maps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var map in enabled.Where(map => map.Value))
        {
            var configPath = MapCacheFiles.Resolve(source, $"_maps/map_jsons/{map.Key}.json");
            await using var stream = File.OpenRead(configPath);
            using var config = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var directory = config.RootElement.GetProperty("map_path").GetString()!;
            var mapFile = config.RootElement.GetProperty("map_file");
            var mapFiles = mapFile.ValueKind == JsonValueKind.Array
                ? mapFile.EnumerateArray().Select(file => file.GetString()!).ToArray()
                : new[] { mapFile.GetString()! };
            foreach (var file in mapFiles)
            {
                var path = $"_maps/{directory}/{file}".Replace('\\', '/');
                if (!IsMap(path))
                    throw new InvalidDataException($"Некорректный путь основной карты: {path}");
                maps.Add(path);
            }
        }
        return maps;
    }

    private static bool IsMapSelectionFile(string path) => path == "config/maps.txt" ||
        (path.StartsWith("_maps/map_jsons/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private static bool IsRenderSource(string path) => IsMap(path) || IsMapSelectionFile(path) ||
        path.EndsWith(".dm", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".dme", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".dmf", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".dmi", StringComparison.OrdinalIgnoreCase) ||
        ((path.StartsWith("icons/", StringComparison.Ordinal) ||
          path.StartsWith("modular_bandastation/", StringComparison.Ordinal) ||
          path.StartsWith("code/", StringComparison.Ordinal)) &&
         ImageExtensions.Contains(Path.GetExtension(path))) ||
        path == "SpacemanDMM.toml";
}
