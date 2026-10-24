using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Loggepector.Models;
using SkiaSharp;

namespace Loggepector.Services;

public sealed class EmojiService : IEmojiService
{
    public static event Action? ImagesChanged;
    public static IReadOnlyDictionary<string, Bitmap> Images { get; private set; } = new Dictionary<string, Bitmap>();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private string? _loadedSource;
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loggepector", "Emoji");
    public bool HasLocalData => Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*.dmi", SearchOption.AllDirectories).Any();

    public async Task PrepareAsync(string repository, Func<MapDownloadStage, CancellationToken, Task<bool>> confirm, CancellationToken token)
    {
        var address = new Uri(repository);
        var segments = address.AbsolutePath.Trim('/').Split('/');
        if (address.Scheme != "https" || address.Host != "github.com" || segments.Length != 2)
            throw new InvalidOperationException("Выберите GitHub-репозиторий станции.");
        var source = $"https://raw.githubusercontent.com/{segments[0]}/{segments[1].Replace(".git", string.Empty, StringComparison.Ordinal)}/HEAD/";
        if (_loadedSource == source)
            return;
        ReplaceImages(new Dictionary<string, Bitmap>());
        _loadedSource = null;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        var directory = Path.Combine(_root, key);
        var paths = new[] { "icons/ui/chat/emoji.dmi", "modular_bandastation/emojis220/icons/emoji220.dmi" };
        var files = paths.Select(path => new MapDownloadFile(Path.GetFileName(path), new Uri(source + path))).ToArray();
        var missing = files.Where(file => !File.Exists(Path.Combine(directory, file.Name))).ToArray();
        if (missing.Length > 0)
        {
            if (!await confirm(new MapDownloadStage("Скачать смайлики?", "Спрайты emoji будут скачаны из выбранного репозитория и сохранены для работы без интернета.", missing), token))
                return;
            Directory.CreateDirectory(directory);
            foreach (var file in missing)
            {
                var bytes = await _http.GetByteArrayAsync(file.Url, token);
                var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, token);
                    File.Move(temporary, Path.Combine(directory, file.Name), true);
                }
                finally { File.Delete(temporary); }
            }
        }
        var images = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in files)
                ReadSprites(await File.ReadAllBytesAsync(Path.Combine(directory, file.Name), token), images);
        }
        catch
        {
            foreach (var bitmap in images.Values) bitmap.Dispose();
            throw;
        }
        ReplaceImages(images);
        _loadedSource = source;
    }

    public void DeleteAll()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        _loadedSource = null;
        ReplaceImages(new Dictionary<string, Bitmap>());
    }

    private static void ReplaceImages(IReadOnlyDictionary<string, Bitmap> images)
    {
        var previous = Images;
        Images = images;
        ImagesChanged?.Invoke();
        foreach (var bitmap in previous.Values) bitmap.Dispose();
    }

    private static void ReadSprites(byte[] bytes, Dictionary<string, Bitmap> images)
    {
        string? metadata = null;
        for (var offset = 8; offset + 12 <= bytes.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type == "zTXt")
            {
                var zero = Array.IndexOf(bytes, (byte)0, offset + 8, length);
                if (zero >= 0 && Encoding.ASCII.GetString(bytes, offset + 8, zero - offset - 8) == "Description")
                {
                    using var input = new MemoryStream(bytes, zero + 2, offset + 8 + length - zero - 2);
                    using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                    using var reader = new StreamReader(zlib);
                    metadata = reader.ReadToEnd();
                }
            }
            offset = checked(offset + length + 12);
        }
        if (metadata is null) throw new InvalidDataException("В DMI отсутствует описание спрайтов.");
        var width = int.Parse(Regex.Match(metadata, @"\bwidth\s*=\s*(\d+)").Groups[1].Value);
        var height = int.Parse(Regex.Match(metadata, @"\bheight\s*=\s*(\d+)").Groups[1].Value);
        using var sheet = SKBitmap.Decode(bytes) ?? throw new InvalidDataException("Не удалось прочитать DMI.");
        var states = Regex.Matches(metadata, """state\s*=\s*"(?<name>[^"]*)"(?<data>.*?)(?=\bstate\s*=|# END DMI)""", RegexOptions.Singleline);
        var index = 0;
        foreach (Match state in states)
        {
            var data = state.Groups["data"].Value;
            var directions = int.Parse(Regex.Match(data, @"\bdirs\s*=\s*(\d+)").Groups[1].Value);
            var frames = int.Parse(Regex.Match(data, @"\bframes\s*=\s*(\d+)").Groups[1].Value);
            var name = state.Groups["name"].Value;
            if (name.Length > 0)
            {
                using var sprite = new SKBitmap();
                var x = index % (sheet.Width / width) * width;
                var y = index / (sheet.Width / width) * height;
                if (!sheet.ExtractSubset(sprite, SKRectI.Create(x, y, width, height)))
                    throw new InvalidDataException("Кадр emoji выходит за пределы DMI.");
                using var encoded = sprite.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = encoded.AsStream();
                var bitmap = new Bitmap(stream);
                if (images.Remove(name, out var old)) old.Dispose();
                images[name] = bitmap;
            }
            index += directions * frames;
        }
    }
}
