using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net;
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
    private const int SourceSchemaVersion = 3;
    private readonly SemaphoreSlim _preparationGate = new(1, 1);
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".gif", ".jpg", ".jpeg", ".bmp", ".webp"
    };
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 16,
        EnableMultipleHttp2Connections = true,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    })
    {
        Timeout = TimeSpan.FromHours(1),
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
    };
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "MapCache");
    private readonly string _settings = Path.Combine(AppContext.BaseDirectory, "map-tools-settings.json");

    public bool HasLocalData => Directory.Exists(_root) && Directory.EnumerateFileSystemEntries(_root)
        .Any(path => Path.GetFileName(path) != "update.lock");

    public MapPreparationService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Loggepector/1.0");
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await _preparationGate.WaitAsync(cancellationToken);
        try
        {
            var target = Path.GetFullPath(_root);
            var expected = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "MapCache"));
            if (!string.Equals(target, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Некорректный путь кеша карт.");
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() =>
            {
                if (Directory.Exists(target))
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                        IgnoreInaccessible = false
                    };
                    foreach (var file in Directory.EnumerateFiles(target, "*", options))
                        File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                    Directory.Delete(target, true);
                }
                File.Delete(_settings);
            });
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    public async Task<bool?> GetChoiceAsync(CancellationToken cancellationToken = default) =>
        File.Exists(_settings)
            ? (await MapCacheFiles.ReadAsync<MapToolsSettings>(_settings, cancellationToken)).Enabled : null;

    public async Task<string> GetRepositoryAsync(CancellationToken cancellationToken = default) =>
        File.Exists(_settings)
            ? (await MapCacheFiles.ReadAsync<MapToolsSettings>(_settings, cancellationToken)).RepositoryUrl
            : new MapToolsSettings(null).RepositoryUrl;

    public Task SaveChoiceAsync(bool enabled, string repositoryUrl, CancellationToken cancellationToken = default)
    {
        var repository = ParseRepository(repositoryUrl);
        return MapCacheFiles.SaveAsync(_settings, new MapToolsSettings(enabled, $"https://github.com/{repository}"), cancellationToken);
    }

    private static string ParseRepository(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Укажите HTTPS-ссылку на репозиторий GitHub.");
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Ссылка должна иметь формат https://github.com/владелец/репозиторий.");
        var repository = string.Join("/", parts);
        return repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repository[..^4] : repository;
    }

    public async Task PrepareAsync(IProgress<string> progress, Func<MapDownloadStage, CancellationToken, Task> confirmDownload,
        CancellationToken cancellationToken = default)
    {
        await _preparationGate.WaitAsync(cancellationToken);
        try
        {
            var repositoryName = ParseRepository(await GetRepositoryAsync(cancellationToken));
            Directory.CreateDirectory(_root);
                    await using var cacheLock = new FileStream(Path.Combine(_root, "update.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
                    var statePath = Path.Combine(_root, "state.json");
            var state = File.Exists(statePath)
                ? await MapCacheFiles.ReadAsync<MapCacheState>(statePath, cancellationToken) : new MapCacheState();
            await EnsureRendererAsync(progress, confirmDownload, cancellationToken);
            progress.Report("Проверка текущего коммита репозитория…");
            using var repository = await GetJsonAsync($"https://api.github.com/repos/{repositoryName}", cancellationToken);
            var branch = repository.RootElement.GetProperty("default_branch").GetString()!;
            using var head = await GetJsonAsync($"https://api.github.com/repos/{repositoryName}/commits/{Uri.EscapeDataString(branch)}",
                cancellationToken);
            var commit = head.RootElement.GetProperty("sha").GetString()!;
            var source = Path.Combine(_root, "source");
            var files = state.SourceFiles;
            if (state.SourceRepository != repositoryName || state.SourceSchemaVersion != SourceSchemaVersion || commit != state.SourceCommit || files.Count == 0 || files.Keys.Any(file => !IsRenderSource(file)) ||
                files.Keys.Any(file => !File.Exists(MapCacheFiles.Resolve(source, file))))
            {
                progress.Report("Сравнение файлов карт, кода и иконок…");
                using var tree = await GetJsonAsync($"https://api.github.com/repos/{repositoryName}/git/trees/{commit}?recursive=1",
                    cancellationToken);
                if (tree.RootElement.GetProperty("truncated").GetBoolean())
                    throw new InvalidDataException("GitHub вернул неполный список файлов репозитория.");
                files = tree.RootElement.GetProperty("tree").EnumerateArray()
                    .Where(item => item.GetProperty("type").GetString() == "blob")
                    .Select(item => (Path: item.GetProperty("path").GetString()!, Sha: item.GetProperty("sha").GetString()!))
                    .Where(item => IsRenderSource(item.Path)).ToDictionary(item => item.Path, item => item.Sha);
                if (!files.ContainsKey("tgstation.dme"))
                    throw new InvalidDataException("В выбранном репозитории отсутствует tgstation.dme.");
                Directory.CreateDirectory(source);
                var changed = files.Where(file => !state.SourceFiles.TryGetValue(file.Key, out var sha) ||
                    sha != file.Value || !File.Exists(MapCacheFiles.Resolve(source, file.Key))).ToArray();
                if (changed.Length > 0)
                {
                    var git = await EnsureGitAsync(progress, confirmDownload, cancellationToken);
                    var downloads = changed.Select(file => new MapDownloadFile(file.Key,
                        new Uri($"https://raw.githubusercontent.com/{repositoryName}/{commit}/" +
                            string.Join("/", file.Key.Split('/').Select(Uri.EscapeDataString))))).ToArray();
                    await confirmDownload(new MapDownloadStage("Скачать файлы для рендера?",
                        $"Карты, код и спрайты {repositoryName}, коммит {commit[..8]}. Будут получены только отсутствующие или изменённые файлы через Git.",
                        downloads), cancellationToken);
                    await MapSourceGit.DownloadAsync(git, repositoryName, branch, commit, Path.Combine(_root,
                            "source-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(repositoryName)))[..12] + ".git"),
                        source, changed, progress, cancellationToken);
                }
                var completed = 0;
                await Parallel.ForEachAsync(changed, new ParallelOptions
                {
                    MaxDegreeOfParallelism = 16,
                    CancellationToken = cancellationToken
                }, async (file, token) =>
                {
                    var destination = MapCacheFiles.Resolve(source, file.Key);
                    var candidate = destination + ".candidate";
                    await VerifyBlobAsync(candidate, file.Value, token);
                    File.Move(candidate, destination, true);
                    var count = Interlocked.Increment(ref completed);
                    if (count % 100 == 0 || count == changed.Length)
                        progress.Report($"Проверка файлов рендера: {count}/{changed.Length}");
                });
                foreach (var removed in state.SourceFiles.Keys.Except(files.Keys))
                    File.Delete(MapCacheFiles.Resolve(source, removed));
                if (state.SourceRepository != repositoryName)
                    state.Maps.Clear();
                state.SourceRepository = repositoryName;
                state.SourceFiles = files;
                state.SourceCommit = commit;
                state.SourceSchemaVersion = SourceSchemaVersion;
                await MapCacheFiles.SaveAsync(statePath, state, cancellationToken);
            }
    
            var stationMaps = await GetStationMapsAsync(source, cancellationToken);
            foreach (var path in stationMaps)
                if (!files.ContainsKey(path))
                    throw new InvalidDataException($"Основная карта из конфигурации не найдена: {path}");
            progress.Report($"{repositoryName.Split('/')[1]} · {commit[..8]} · карт: {stationMaps.Count}");
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetMapsAsync(CancellationToken cancellationToken = default)
    {
        await _preparationGate.WaitAsync(cancellationToken);
        try
        {
    var statePath = Path.Combine(_root, "state.json");
            if (!File.Exists(statePath))
                return Array.Empty<string>();
            var state = await MapCacheFiles.ReadAsync<MapCacheState>(statePath, cancellationToken);
            var source = Path.Combine(_root, "source");
            var stationMaps = await GetStationMapsAsync(source, cancellationToken);
            var names = new List<string>();
            if (state.SourceFiles.ContainsKey(AdditionalMaps.PlanetaryPortPath))
                names.Add(AdditionalMaps.PlanetaryPortName);
            if (state.SourceFiles.ContainsKey(AdditionalMaps.LavalandPath))
                names.Add(AdditionalMaps.LavalandName);
            foreach (var configPath in state.SourceFiles.Keys.Where(path => path.StartsWith("_maps/map_jsons/", StringComparison.Ordinal)))
            {
                using var config = JsonDocument.Parse(await File.ReadAllTextAsync(MapCacheFiles.Resolve(source, configPath), cancellationToken));
                var files = GetMapFiles(config.RootElement);
                if (files.All(stationMaps.Contains))
                    names.Add(config.RootElement.GetProperty("map_name").GetString()!);
            }
            return names.Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    private static HashSet<string> GetMapFiles(JsonElement config)
    {
        var directory = config.GetProperty("map_path").GetString()!;
        var element = config.GetProperty("map_file");
        var files = element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(file => file.GetString()!).ToArray() : new[] { element.GetString()! };
        return files.Select(file => $"_maps/{directory}/{file}".Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<HashSet<string>> FindMapFilesAsync(string source, MapCacheState state, string mapName, CancellationToken cancellationToken)
    {
        static string Normalize(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (Normalize(mapName) == Normalize(AdditionalMaps.PlanetaryPortName))
            return new HashSet<string>(StringComparer.Ordinal) { AdditionalMaps.PlanetaryPortPath };
        if (Normalize(mapName) == Normalize(AdditionalMaps.LavalandName))
            return new HashSet<string>(StringComparer.Ordinal) { AdditionalMaps.LavalandPath };
        foreach (var configPath in state.SourceFiles.Keys.Where(path => path.StartsWith("_maps/map_jsons/", StringComparison.Ordinal)))
        {
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(MapCacheFiles.Resolve(source, configPath), cancellationToken));
            if (Normalize(config.RootElement.GetProperty("map_name").GetString()!) == Normalize(mapName) ||
                Normalize(Path.GetFileNameWithoutExtension(configPath)) == Normalize(mapName))
                return GetMapFiles(config.RootElement);
        }
        throw new InvalidOperationException($"Карта «{mapName}» не найдена в репозитории.");
    }

    public async Task RenderAsync(string mapName, IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        await _preparationGate.WaitAsync(cancellationToken);
        try
        {
    await using var cacheLock = new FileStream(Path.Combine(_root, "update.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            var statePath = Path.Combine(_root, "state.json");
            var state = await MapCacheFiles.ReadAsync<MapCacheState>(statePath, cancellationToken);
            var files = state.SourceFiles;
            var commit = state.SourceCommit ?? throw new InvalidDataException("В кеше отсутствует коммит карт.");
            var source = Path.Combine(_root, "source");
            var renderer = Path.Combine(_root, "tools", MapToolPlatform.RendererName);
            var dependencies = string.Join("\n", files.Where(file => !IsMap(file.Key) && !IsMapSelectionFile(file.Key))
                .OrderBy(file => file.Key, StringComparer.Ordinal).Select(file => file.Key + ":" + file.Value));
            await using (var binary = File.OpenRead(renderer))
                dependencies += Convert.ToHexString(await SHA256.HashDataAsync(binary, cancellationToken));
            var dependencyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dependencies)));
            var selected = await FindMapFilesAsync(source, state, mapName, cancellationToken);
            var maps = files.Where(file => selected.Contains(file.Key)).OrderBy(file => file.Key, StringComparer.Ordinal).ToArray();
            if (maps.Length != selected.Count)
                throw new InvalidDataException("Не все файлы выбранной карты доступны в кеше.");
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
            await MapCacheFiles.SaveAsync(statePath, state, cancellationToken);
            progress.Report($"{mapName} · {commit[..8]}");
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    private async Task<string> EnsureGitAsync(IProgress<string> progress,
        Func<MapDownloadStage, CancellationToken, Task> confirmDownload, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return MapSourceGit.FindExecutable()
                ?? throw new FileNotFoundException("Для загрузки карт на Linux нужен Git в PATH. Установите пакет git.");
        var bundled = Path.Combine(_root, "tools", "mingit", "cmd", "git.exe");
        if (File.Exists(bundled))
            return bundled;
        if (MapSourceGit.FindExecutable() is { } installed)
            return installed;
        progress.Report("Проверка релиза MinGit…");
        using var release = await GetJsonAsync("https://api.github.com/repos/git-for-windows/git/releases/latest", cancellationToken);
        var asset = release.RootElement.GetProperty("assets").EnumerateArray().Single(item =>
        {
            var name = item.GetProperty("name").GetString()!;
            return name.StartsWith("MinGit-", StringComparison.Ordinal) &&
                   name.EndsWith("-64-bit.zip", StringComparison.Ordinal) &&
                   !name.Contains("busybox", StringComparison.OrdinalIgnoreCase);
        });
        var digest = asset.GetProperty("digest").GetString()
            ?? throw new InvalidDataException("GitHub не предоставил SHA256 архива MinGit.");
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal))
            throw new InvalidDataException("Неизвестный формат контрольной суммы MinGit.");
        await confirmDownload(new MapDownloadStage("Скачать MinGit?",
            "Переносимый Git нужен для пакетной загрузки файлов карт. Источник: официальный релиз Git for Windows.",
            new[] { new MapDownloadFile(asset.GetProperty("name").GetString()!,
                new Uri(asset.GetProperty("browser_download_url").GetString()!), asset.GetProperty("size").GetInt64()) }), cancellationToken);
        progress.Report("Загрузка MinGit…");
        var package = Path.Combine(_root, "tools", "mingit.zip.candidate");
        await DownloadAsync(asset.GetProperty("browser_download_url").GetString()!, package, cancellationToken);
        await using (var stream = File.OpenRead(package))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!string.Equals(actual, digest[7..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA256 архива MinGit не совпадает с официальным релизом.");
        }
        progress.Report("Распаковка MinGit…");
        var directory = Path.Combine(_root, "tools", "mingit");
        var staging = directory + ".partial-" + Guid.NewGuid().ToString("N");
        await Task.Run(() => ZipFile.ExtractToDirectory(package, staging), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(Path.Combine(staging, "cmd", "git.exe")))
            throw new InvalidDataException("Архив MinGit не содержит cmd/git.exe.");
        Directory.Move(staging, directory);
        File.Delete(package);
        return bundled;
    }

    private async Task<string> EnsureRendererAsync(IProgress<string> progress,
        Func<MapDownloadStage, CancellationToken, Task> confirmDownload, CancellationToken cancellationToken)
    {
        var name = MapToolPlatform.RendererName;
        var renderer = Path.Combine(_root, "tools", name);
        if (File.Exists(renderer))
        {
            MapToolPlatform.MakeExecutable(renderer);
            return renderer;
        }
        progress.Report("Проверка релиза dmm-tools…");
        using var release = await GetJsonAsync("https://api.github.com/repos/SpaceManiac/SpacemanDMM/releases/latest",
            cancellationToken);
        var asset = release.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(item =>
            string.Equals(item.GetProperty("name").GetString(), name, StringComparison.Ordinal));
        if (asset.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException($"В официальном релизе SpacemanDMM отсутствует рендерер {name} для текущей ОС.");
        await confirmDownload(new MapDownloadStage("Скачать рендерер карт?",
            "dmm-tools отрисовывает карты станции. Источник: официальный релиз SpacemanDMM.",
            new[] { new MapDownloadFile(name, new Uri(asset.GetProperty("browser_download_url").GetString()!),
                asset.GetProperty("size").GetInt64()) }), cancellationToken);
        progress.Report("Загрузка dmm-tools…");
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
        MapToolPlatform.MakeExecutable(candidate);
        File.Move(candidate, renderer, true);
        return renderer;
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var temporary = destination + ".download";
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(temporary))
                await input.CopyToAsync(output, cancellationToken);
            File.Move(temporary, destination, true);
        }
        finally
        {
            File.Delete(temporary);
        }
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
