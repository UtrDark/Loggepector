using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Models;
using Loggepector.Utils;
using SkiaSharp;

namespace Loggepector.Services;

internal static class MapRenderer
{
    internal static async Task<string[]> RenderAsync(string executable, string source, string map,
        string output, string commit, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(output);
        var full = Path.Combine(output, "full");
        Directory.CreateDirectory(full);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = source,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-e", Path.Combine(source, "tgstation.dme"), "minimap", "-o", full,
                     MapCacheFiles.Resolve(source, map) })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить dmm-tools.");
        using var cancel = cancellationToken.Register(() =>
        {
            if (!process.HasExited)
                process.Kill(true);
        });
        var standard = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var log = await standard + "\n" + await errors;
        await File.WriteAllTextAsync(Path.Combine(output, "render.log"), log, cancellationToken);
        if (process.ExitCode != 0 || log.Contains("there were some parsing errors", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Ошибка рендера {map}. Подробности: {output}/render.log");

        var images = Directory.GetFiles(full, "*.png");
        if (images.Length == 0)
            throw new InvalidDataException($"Рендер не создал PNG для {map}.");
        var chunks = new List<MapChunk>();
        var files = new List<string>();
        var chunksDirectory = Path.Combine(output, "chunks");
        Directory.CreateDirectory(chunksDirectory);
        foreach (var imagePath in images.OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report($"Нарезка {Path.GetFileName(imagePath)}");
            var name = Path.GetFileNameWithoutExtension(imagePath);
            var z = int.Parse(name[(name.LastIndexOf('-') + 1)..]);
            using var image = SKBitmap.Decode(imagePath)
                ?? throw new InvalidDataException($"Не удалось прочитать {imagePath}.");
            if (image.Width % 32 != 0 || image.Height % 32 != 0)
                throw new InvalidDataException($"Размер изображения {imagePath} не кратен размеру тайла 32.");
            var columns = (image.Width + 1023) / 1024;
            var rows = (image.Height + 1023) / 1024;
            var layerChunks = new MapChunk[columns * rows];
            var completed = 0;
            await Parallel.ForEachAsync(Enumerable.Range(0, layerChunks.Length), new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 4),
                CancellationToken = cancellationToken
            }, (index, token) =>
            {
                token.ThrowIfCancellationRequested();
                var top = index / columns * 1024;
                var left = index % columns * 1024;
                var width = Math.Min(1024, image.Width - left);
                var height = Math.Min(1024, image.Height - top);
                using var pixels = image.PeekPixels()
                    ?? throw new InvalidDataException("Не удалось получить пиксели карты.");
                using var tile = pixels.ExtractSubset(new SKRectI(left, top, left + width, top + height))
                    ?? throw new InvalidDataException("Не удалось получить область чанка.");
                using var padded = width == 1024 && height == 1024 ? null
                    : new SKBitmap(1024, 1024, SKColorType.Rgba8888, SKAlphaType.Premul);
                if (padded is not null)
                {
                    using var canvas = new SKCanvas(padded);
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawBitmap(image, new SKRect(left, top, left + width, top + height),
                        new SKRect(0, 0, width, height));
                }
                using var paddedPixels = padded?.PeekPixels();
                var file = $"chunks/z{z}_x{left / 1024:D2}_y{top / 1024:D2}.png";
                using var encoded = (paddedPixels ?? tile).Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub, 1))
                    ?? throw new InvalidDataException("Не удалось закодировать PNG-чанк.");
                using (var stream = File.Create(MapCacheFiles.Resolve(output, file)))
                    encoded.SaveTo(stream);
                layerChunks[index] = new MapChunk(file, z, left / 1024, top / 1024, width, height,
                    left / 32 + 1, (left + width) / 32, (image.Height - top - height) / 32 + 1,
                    (image.Height - top) / 32);
                var count = Interlocked.Increment(ref completed);
                if (count % 8 == 0 || count == layerChunks.Length)
                    progress.Report($"Нарезка {name}: {count}/{layerChunks.Length}");
                return ValueTask.CompletedTask;
            });
            chunks.AddRange(layerChunks);
            files.AddRange(layerChunks.Select(chunk => chunk.File));
            File.Delete(imagePath);
        }
        await MapCacheFiles.SaveAsync(Path.Combine(output, "manifest.json"),
            new MapTileManifest(map, commit, 32, 1024, "top-left; game Y increases bottom-to-top", chunks.ToArray()),
            cancellationToken);
        files.Add("manifest.json");
        return files.ToArray();
    }
}
