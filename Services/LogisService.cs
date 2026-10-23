using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using Loggepector.Models;

namespace Loggepector.Services;

public sealed class LogisService : ILogisService, IDisposable
{
    private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "logis-settings.json");
    private readonly string _sessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loggepector", "logis-session.json");
    private HttpClient? _client;
    private CookieContainer? _cookies;
    private string? _savedSession;
    public Uri? ServerAddress { get; private set; }
    public string? SessionId { get; private set; }
    public string? SessionCookieName { get; private set; }

    public string LoadAddress() => File.Exists(_settingsPath)
        ? JsonSerializer.Deserialize(File.ReadAllText(_settingsPath), AppJsonContext.Default.String)
          ?? throw new InvalidDataException("Не указан адрес Logis в настройках.")
        : string.Empty;

    public void SaveAddress(Uri address)
    {
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(address.AbsoluteUri, AppJsonContext.Default.String));
        File.Move(temporary, _settingsPath, true);
    }

    public void RestoreSession()
    {
        if (_client is not null || !File.Exists(_sessionPath))
            return;
        var saved = JsonSerializer.Deserialize(File.ReadAllText(_sessionPath), AppJsonContext.Default.LogisSessionSettings)
            ?? throw new InvalidDataException("Файл сессии Logis пуст.");
        if (!Uri.TryCreate(saved.Address, UriKind.Absolute, out var address) || address.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("В сессии Logis сохранён некорректный адрес.");
        if (!string.Equals(address.AbsoluteUri, LoadAddress(), StringComparison.Ordinal))
            return;
        if (saved.Expires != DateTime.MinValue && saved.Expires.ToUniversalTime() <= DateTime.UtcNow)
        {
            ClearSession();
            return;
        }
        SetSession(address, new Cookie(saved.Name, saved.Value, saved.Path, saved.Domain)
        {
            Secure = saved.Secure,
            HttpOnly = saved.HttpOnly,
            Expires = saved.Expires
        });
    }

    public void SetSession(Uri address, Cookie cookie)
    {
        var cookies = new CookieContainer();
        cookies.Add(cookie);
        if (cookies.GetCookies(address)[cookie.Name] is null || cookie.Expired || string.IsNullOrEmpty(cookie.Value))
            throw new InvalidOperationException("Выбранная cookie не относится к серверу Logis или истекла.");
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        _client?.Dispose();
        _client = client;
        ServerAddress = address;
        SessionCookieName = cookie.Name;
        SessionId = cookie.Value;
        _cookies = cookies;
        SaveSession();
    }

    public async Task<IReadOnlyList<string>> GetServersAsync(CancellationToken cancellationToken)
    {
        using var request = CreateRequest(("self", "buildList"));
        var servers = await ReadJsonAsync(request, AppJsonContext.Default.LogisStrings, cancellationToken);
        return servers.Distinct(StringComparer.Ordinal).OrderBy(server => server, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<string>> GetDatesAsync(string server, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(("self", "datesList"), ("server", server));
        var dates = await ReadJsonAsync(request, AppJsonContext.Default.LogisStrings, cancellationToken);
        return dates.Distinct(StringComparer.Ordinal).OrderByDescending(date => date, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<LogisLogFile>> GetLogsAsync(string server, string date, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(("self", "logsFolder"), ("server", server), ("date", date));
        var folders = await ReadJsonAsync(request, AppJsonContext.Default.LogisFolders, cancellationToken);
        return folders.Where(folder => folder.Value.Contains("game.log", StringComparer.Ordinal) &&
                !folder.Key.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("secret", StringComparer.OrdinalIgnoreCase))
            .Select(folder => new LogisLogFile(folder.Key))
            .OrderByDescending(log => log.RoundNumber).ThenBy(log => log.Folder, StringComparer.Ordinal).ToArray();
    }

    public async Task DownloadGameLogAsync(string server, string date, LogisLogFile log, string destination,
        IProgress<int> progress, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(("self", "batch"), ("dir", $"{server}/{date}"), ("file", "game.log"));
        if (log.Folder.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains("secret", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Загрузка secret-логов отключена.");
        request.Headers.Add("List", log.Folder.Replace("/", string.Empty, StringComparison.Ordinal));
        using var response = await SendAsync(request, cancellationToken);
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not "text/plain" and not "application/octet-stream")
            throw new InvalidDataException("Logis вернул ответ вместо game.log. Возможно, требуется повторный вход.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        var total = response.Content.Headers.ContentLength;
        long received = 0;
        var lastProgress = -1;
        progress.Report(0);
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                break;
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            received += count;
            var value = total is > 0 ? (int)Math.Min(99, received * 100 / total.Value) : 0;
            if (value != lastProgress)
            {
                progress.Report(value);
                lastProgress = value;
            }
        }
        if (received == 0)
            throw new InvalidDataException("Logis вернул пустой game.log.");
        progress.Report(100);
    }

    private HttpRequestMessage CreateRequest(params (string Name, string Value)[] parameters)
    {
        var address = ServerAddress ?? throw new InvalidOperationException("Сначала подключитесь к Logis.");
        var uri = new UriBuilder(address)
        {
            Query = string.Join("&", parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value)}")),
            Fragment = string.Empty
        };
        return new HttpRequestMessage(HttpMethod.Get, uri.Uri);
    }

    private async Task<T> ReadJsonAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(request, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonSerializer.DeserializeAsync(stream, typeInfo, cancellationToken)
                ?? throw new InvalidDataException("Logis вернул пустой ответ списка.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Ответ Logis не соответствует формату списка. Возможно, требуется повторный вход.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = _client ?? throw new InvalidOperationException("Сначала подключитесь к Logis.");
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                SaveSession();
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }
        var status = response.StatusCode;
        response.Dispose();
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || (int)status is >= 300 and < 400)
        {
            ClearSession();
            throw new UnauthorizedAccessException("Сессия Logis истекла или нет доступа. Войдите заново.");
        }
        if (status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Logis ограничил запросы. Подождите 30 секунд и повторите попытку.");
        throw new HttpRequestException($"Ошибка Logis: HTTP {(int)status}.");
    }

    private void SaveSession()
    {
        var address = ServerAddress ?? throw new InvalidOperationException("Нет адреса сессии Logis.");
        var cookie = _cookies?.GetCookies(address)[SessionCookieName!];
        if (cookie is null || cookie.Expired || string.IsNullOrEmpty(cookie.Value))
        {
            ClearSession();
            throw new UnauthorizedAccessException("Сессия Logis больше не доступна. Войдите заново.");
        }
        SessionId = cookie.Value;
        var saved = new LogisSessionSettings
        {
            Address = address.AbsoluteUri,
            Name = cookie.Name,
            Value = cookie.Value,
            Domain = cookie.Domain,
            Path = cookie.Path,
            Secure = cookie.Secure,
            HttpOnly = cookie.HttpOnly,
            Expires = cookie.Expires
        };
        var json = JsonSerializer.Serialize(saved, AppJsonContext.Default.LogisSessionSettings);
        if (json == _savedSession)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(_sessionPath)!);
        var temporary = _sessionPath + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(temporary, options))
        using (var writer = new StreamWriter(stream))
            writer.Write(json);
        File.Move(temporary, _sessionPath, true);
        _savedSession = json;
    }

    private void ClearSession()
    {
        _client?.Dispose();
        _client = null;
        _cookies = null;
        _savedSession = null;
        ServerAddress = null;
        SessionId = null;
        SessionCookieName = null;
        File.Delete(_sessionPath);
    }

    public void Dispose() => _client?.Dispose();
}
