using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Loggepector.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Loggepector.Utils;

internal static class MapCacheFiles
{
    private static JsonTypeInfo<T> GetTypeInfo<T>() =>
        (JsonTypeInfo<T>)(AppJsonContext.Default.GetTypeInfo(typeof(T))
            ?? throw new InvalidOperationException($"Не настроена JSON-сериализация для {typeof(T).Name}."));

    internal static string Resolve(string root, string relative)
    {
        var directory = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, relative));
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Путь выходит за пределы папки карт: {relative}");
        return path;
    }

    internal static async Task<T> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await JsonSerializer.DeserializeAsync(stream, GetTypeInfo<T>(), cancellationToken)
            ?? throw new InvalidDataException($"Пустой файл настроек: {path}");
    }

    internal static async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, value, GetTypeInfo<T>(), cancellationToken);
        File.Move(temporary, path, true);
    }
}
