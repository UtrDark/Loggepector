using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;

namespace Loggepector.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<ReleaseUpdate> CheckAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "build-info.json");
        if (!File.Exists(path))
            return new ReleaseUpdate(null, "Сборка без build-info.json: проверка релизов отключена.");
        using var build = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        var repository = build.RootElement.GetProperty("repository").GetString();
        var commit = build.RootElement.GetProperty("commit").GetString();
        var tag = build.RootElement.GetProperty("tag").GetString();
        if (!Uri.TryCreate(repository, UriKind.Absolute, out var address) || address.Scheme != "https" ||
            address.Host != "github.com" || address.UserInfo.Length > 0 ||
            commit is null || commit.Length != 40 || !commit.All(Uri.IsHexDigit))
            throw new InvalidDataException("Некорректные repository или commit в build-info.json.");
        var parts = address.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Некорректный repository в build-info.json.");
        var owner = parts[0];
        var name = parts[1];
        var api = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, api + "/releases/latest");
        request.Headers.UserAgent.ParseAdd("Loggepector/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new ReleaseUpdate(null, "Опубликованные релизы не найдены.");
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var latestTag = release.RootElement.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("GitHub не вернул тег latest-релиза.");
        var description = $"Сборка: {tag} ({commit[..7]}). Latest: {latestTag}.";
        if (string.Equals(tag, latestTag, StringComparison.Ordinal))
            return new ReleaseUpdate(null, description);
        if (string.IsNullOrEmpty(tag))
        {
            using var compareRequest = new HttpRequestMessage(HttpMethod.Get,
                api + "/compare/" + commit + "..." + Uri.EscapeDataString(latestTag));
            compareRequest.Headers.UserAgent.ParseAdd("Loggepector/1.0");
            compareRequest.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var compareResponse = await _http.SendAsync(compareRequest, cancellationToken).ConfigureAwait(false);
            compareResponse.EnsureSuccessStatusCode();
            using var comparison = JsonDocument.Parse(await compareResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (comparison.RootElement.GetProperty("status").GetString() != "ahead")
                return new ReleaseUpdate(null, description);
        }
        return new ReleaseUpdate(new Uri($"https://github.com/{owner}/{name}/releases/tag/{Uri.EscapeDataString(latestTag)}"), description);
    }
}
