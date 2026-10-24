using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public sealed class BigLogService(IBigLogParser parser) : IBigLogService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly string _tools = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Loggepector", "Tools", "7zip");

    public async Task<BigLogData> LoadAsync(string path, Func<MapDownloadStage, CancellationToken, Task<bool>> confirmDownload,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (Directory.Exists(path))
            return await parser.ParseAsync(path, progress, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(path) || !Path.GetExtension(path).Equals(".7z", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Выберите папку раунда или .7z архив.");
        var executable = FindArchiver() ?? await DownloadArchiverAsync(confirmDownload, cancellationToken).ConfigureAwait(false);
        var temporary = Path.Combine(Path.GetTempPath(), "Loggepector", "big-logs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var filters = BigLogParser.FileNames.Select(name => "-ir!" + name).Append("-xr!secret").ToArray();
            var listing = await RunAsync(executable, new[] { "l", Path.GetFullPath(path), "-slt", "-sccUTF-8", "-p-" }
                .Concat(filters), cancellationToken).ConfigureAwait(false);
            ValidateListing(listing);
            progress?.Report(1);
            await RunAsync(executable, new[] { "x", Path.GetFullPath(path), "-y", "-bd", "-sccUTF-8", "-p-", "-o" + temporary }
                .Concat(filters), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await parser.ParseAsync(temporary, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(temporary, "*", new EnumerationOptions
                     { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(temporary, true);
        }
    }

    private string? FindArchiver()
    {
        var names = OperatingSystem.IsWindows() ? new[] { "7z.exe", "7za.exe", "7zz.exe" } : new[] { "7zz", "7z", "7za" };
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim('"')).ToList();
        if (OperatingSystem.IsWindows())
        {
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip"));
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip"));
        }
        directories.Add(_tools);
        foreach (var directory in directories)
            foreach (var name in names)
                if (File.Exists(Path.Combine(directory, name)))
                    return Path.Combine(directory, name);
        return null;
    }

    private async Task<string> DownloadArchiverAsync(Func<MapDownloadStage, CancellationToken, Task<bool>> confirmDownload,
        CancellationToken cancellationToken)
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Loggepector/1.0");
        using var releaseResponse = await _http.GetAsync("https://api.github.com/repos/ip7z/7zip/releases/latest", cancellationToken).ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await releaseResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var assets = release.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var windows = OperatingSystem.IsWindows();
        var suffix = (OperatingSystem.IsLinux(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture) switch
        {
            (true, _, Architecture.X64) => "-linux-x64.tar.xz",
            (true, _, Architecture.Arm64) => "-linux-arm64.tar.xz",
            (_, true, _) => "-mac.tar.xz",
            _ when windows => "-extra.7z",
            _ => throw new PlatformNotSupportedException("Установите системный 7-Zip для этой платформы.")
        };
        var package = assets.Single(asset => asset.GetProperty("name").GetString()!.EndsWith(suffix, StringComparison.Ordinal));
        var downloads = new List<JsonElement> { package };
        if (windows)
            downloads.Add(assets.Single(asset => asset.GetProperty("name").GetString() == "7zr.exe"));
        var files = downloads.Select(asset => new MapDownloadFile(asset.GetProperty("name").GetString()!,
            new Uri(asset.GetProperty("browser_download_url").GetString()!), asset.GetProperty("size").GetInt64())).ToArray();
        if (!await confirmDownload(new MapDownloadStage("Скачать portable 7-Zip?",
                "Системный 7-Zip не найден. Будет загружена консольная portable-версия из официального релиза ip7z/7zip, без установки в систему.", files), cancellationToken).ConfigureAwait(false))
            throw new OperationCanceledException("Загрузка архиватора отменена.");
        Directory.CreateDirectory(_tools);
        var staging = Path.Combine(_tools, "download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var asset in downloads)
                await DownloadAssetAsync(asset, Path.Combine(staging, asset.GetProperty("name").GetString()!), cancellationToken).ConfigureAwait(false);
            if (windows)
            {
                var bootstrap = Path.Combine(staging, "7zr.exe");
                await RunAsync(bootstrap, new[] { "x", "-y", "-o" + staging, Path.Combine(staging, package.GetProperty("name").GetString()!),
                    "7za.exe", "x64/7za.exe" }, cancellationToken).ConfigureAwait(false);
                var binary = RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "7za.exe" : Path.Combine("x64", "7za.exe");
                File.Move(Path.Combine(staging, binary), Path.Combine(_tools, "7za.exe"), true);
                return Path.Combine(_tools, "7za.exe");
            }
            await RunAsync("tar", new[] { "-xJf", Path.Combine(staging, package.GetProperty("name").GetString()!), "-C", staging, "7zz" }, cancellationToken).ConfigureAwait(false);
            var renderer = Path.Combine(staging, "7zz");
            File.SetUnixFileMode(renderer, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.Move(renderer, Path.Combine(_tools, "7zz"), true);
            return Path.Combine(_tools, "7zz");
        }
        finally
        {
            Directory.Delete(staging, true);
        }
    }

    private async Task DownloadAssetAsync(JsonElement asset, string destination, CancellationToken cancellationToken)
    {
        var digest = asset.GetProperty("digest").GetString()
            ?? throw new InvalidDataException("В релизе архиватора отсутствует контрольная сумма.");
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal))
            throw new InvalidDataException("Неизвестный формат контрольной суммы архиватора.");
        using var response = await _http.GetAsync(asset.GetProperty("browser_download_url").GetString()!,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(destination))
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await using var stream = File.OpenRead(destination);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Контрольная сумма скачанного архиватора не совпадает с релизом.");
    }

    private static void ValidateListing(string listing)
    {
        var entriesStarted = false;
        foreach (var line in listing.Split('\n'))
        {
            if (!entriesStarted)
            {
                entriesStarted = line.TrimEnd('\r') == "----------";
                continue;
            }
            if (line.StartsWith("Symbolic Link = ", StringComparison.Ordinal) || line.StartsWith("Hard Link = ", StringComparison.Ordinal))
                throw new InvalidDataException("Архив логов содержит ссылки вместо обычных файлов.");
            if (!line.StartsWith("Path = ", StringComparison.Ordinal))
                continue;
            var path = line[7..].TrimEnd('\r').Replace('\\', '/');
            if (Path.IsPathRooted(path) || path.Contains(':') || path.Split('/').Contains("..", StringComparer.Ordinal))
                throw new InvalidDataException("В архиве найден путь за пределами папки распаковки.");
        }
        if (!entriesStarted)
            throw new InvalidDataException("Архиватор не вернул технический список файлов архива.");
    }

    private static async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить архиватор.");
        using var registration = cancellationToken.Register(() =>
        {
            if (!process.HasExited)
                process.Kill(true);
        });
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var result = await output.ConfigureAwait(false);
        var errorText = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new IOException($"{executable} завершился с кодом {process.ExitCode}:\n{errorText.Trim()}\n{result.Trim()}");
        return result;
    }
}
