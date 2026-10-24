using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public sealed class RoundEndStyleService : IRoundEndStyleService
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loggepector", "Roundend");
    public bool HasLocalData => Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*.css").Any();
    public void DeleteAll()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public async Task<string?> LoadAsync(string repository, Func<MapDownloadStage, CancellationToken, Task<bool>> confirmDownload,
        CancellationToken cancellationToken)
    {
        var address = new Uri(repository);
        var segments = address.AbsolutePath.Trim('/').Split('/');
        if (address.Scheme != "https" || address.Host != "github.com" || segments.Length != 2)
            throw new InvalidOperationException("Для оформления Roundend выберите GitHub-репозиторий станции через «Выбрать репу».");
        var source = $"https://raw.githubusercontent.com/{segments[0]}/{segments[1].Replace(".git", string.Empty, StringComparison.Ordinal)}/HEAD/";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var directory = _root;
        var cache = Path.Combine(directory, key + ".css");
        if (File.Exists(cache))
            return await File.ReadAllTextAsync(cache, cancellationToken).ConfigureAwait(false);
        var fonts = new[] { "fa-regular-400.woff2", "fa-solid-900.woff2", "fa-v4compatibility.woff2" };
        var paths = new[] { "html/browser/roundend.css", "html/font-awesome/css/all.min.css" }
            .Concat(fonts.Select(name => "html/font-awesome/webfonts/" + name)).ToArray();
        var files = paths.Select(path => new MapDownloadFile(Path.GetFileName(path), new Uri(source + path))).ToArray();
        if (!await confirmDownload(new MapDownloadStage("Скачать оформление Roundend?",
                "Будут скачаны оригинальные CSS станции и шрифты иконок. Оформление сохранится локально для открытия отчётов без интернета.", files), cancellationToken).ConfigureAwait(false))
            return null;
        var css = await _http.GetStringAsync(files[0].Url, cancellationToken).ConfigureAwait(false);
        var icons = await _http.GetStringAsync(files[1].Url, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < fonts.Length; i++)
        {
            var bytes = await _http.GetByteArrayAsync(files[i + 2].Url, cancellationToken).ConfigureAwait(false);
            icons = icons.Replace(fonts[i], "data:font/woff2;base64," + Convert.ToBase64String(bytes), StringComparison.Ordinal);
        }
        var content = css + "\n" + icons;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, cache, true);
        }
        finally
        {
            File.Delete(temporary);
        }
        return content;
    }
}
