using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Loggepector.Utils;

namespace Loggepector.Services;

internal static class MapSourceGit
{
    internal static string? FindExecutable()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            var path = Path.Combine(directory.Trim('"'), MapToolPlatform.GitName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    internal static async Task DownloadAsync(string git, string repository, string branch, string commit,
        string cache, string source, KeyValuePair<string, string>[] files,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (files.Length == 0)
            return;
        if (!Directory.Exists(cache))
        {
            progress.Report("Git: получение структуры репозитория без содержимого файлов…");
            var staging = cache + ".partial-" + Guid.NewGuid().ToString("N");
            await RunAsync(git, null, new[] { "clone", "--progress", "--bare", "--filter=blob:none", "--depth=1", "--no-tags",
                "--single-branch", "--branch", branch, "https://github.com/" + repository + ".git", staging },
                null, progress, cancellationToken);
            Directory.Move(staging, cache);
        }
        await RunAsync(git, cache, new[] { "fetch", "--progress", "--depth=1", "--filter=blob:none", "--no-tags",
            "--no-write-fetch-head", "origin", commit }, null, progress, cancellationToken);
        progress.Report($"Git: пакетная загрузка {files.Length} файлов рендера…");
        await RunAsync(git, cache, new[] { "-c", "fetch.negotiationAlgorithm=noop", "fetch", "--progress", "--filter=blob:none",
            "--no-tags", "--no-write-fetch-head", "--recurse-submodules=no", "--stdin", "origin" },
            files.Select(file => file.Value).Distinct().ToArray(), progress, cancellationToken);
        using var process = Start(git, cache, new[] { "cat-file", "--batch" }, true);
        using var cancel = cancellationToken.Register(() => Stop(process));
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        await using var data = new BufferedStream(process.StandardOutput.BaseStream, 81920);
        var input = WriteInputAsync(process, files.Select(file => file.Value).ToArray(), cancellationToken);
        try
        {
            var headerBuffer = new byte[1];
            var buffer = new byte[81920];
            for (var index = 0; index < files.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var header = new StringBuilder();
                while (true)
                {
                    await data.ReadExactlyAsync(headerBuffer, cancellationToken);
                    if (headerBuffer[0] == (byte)'\n')
                        break;
                    if (header.Length > 200)
                        throw new InvalidDataException("Некорректный заголовок git cat-file.");
                    header.Append((char)headerBuffer[0]);
                }
                var parts = header.ToString().Split(' ');
                if (parts.Length != 3 || parts[0] != files[index].Value || parts[1] != "blob" ||
                    !long.TryParse(parts[2], out var remaining) || remaining < 0)
                    throw new InvalidDataException($"Git не предоставил содержимое {files[index].Key}: {header}");
                var destination = MapCacheFiles.Resolve(source, files[index].Key) + ".candidate";
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var output = File.Create(destination))
                    while (remaining > 0)
                    {
                        var read = await data.ReadAsync(
                            buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                        if (read == 0)
                            throw new EndOfStreamException($"Git прервал передачу {files[index].Key}.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                        remaining -= read;
                    }
                await data.ReadExactlyAsync(headerBuffer, cancellationToken);
                if (headerBuffer[0] != (byte)'\n')
                    throw new InvalidDataException("Некорректный разделитель git cat-file.");
                if ((index + 1) % 100 == 0 || index + 1 == files.Length)
                    progress.Report($"Распаковка Git: {index + 1}/{files.Length}");
            }
            await input;
            await process.WaitForExitAsync(cancellationToken);
            var error = await errors;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Git cat-file: {error}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Stop(process);
            throw;
        }
        catch (Exception exception)
        {
            Stop(process);
            Exception failure = exception;
            try
            {
                await Task.WhenAll(input, process.WaitForExitAsync());
            }
            catch (Exception shutdownException)
            {
                failure = new AggregateException(exception, shutdownException);
            }
            var error = await errors;
            throw new InvalidOperationException($"Ошибка распаковки Git: {error} {exception.Message}", failure);
        }
    }

    private static Process Start(string git, string? directory, IReadOnlyList<string> arguments, bool redirectInput)
    {
        var start = new ProcessStartInfo(git)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput
        };
        if (redirectInput)
            start.StandardInputEncoding = new UTF8Encoding(false);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.longpaths=true");
        if (directory is not null)
        {
            start.ArgumentList.Add("--git-dir=" + directory);
        }
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new Win32Exception("Не удалось запустить Git.");
    }

    private static async Task WriteInputAsync(Process process, IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        process.StandardInput.NewLine = "\n";
        foreach (var line in lines)
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        process.StandardInput.Close();
    }

    private static async Task RunAsync(string git, string? directory, string[] arguments, string[]? input,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        using var process = Start(git, directory, arguments, input is not null);
        using var cancel = cancellationToken.Register(() => Stop(process));
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = ReadProgressAsync(process, progress, cancellationToken);
        IOException? inputError = null;
        if (input is not null)
        {
            try
            {
                await WriteInputAsync(process, input, cancellationToken);
            }
            catch (IOException exception)
            {
                inputError = exception;
            }
        }
        await process.WaitForExitAsync(cancellationToken);
        var error = await errors;
        await output;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Ошибка Git ({string.Join(" ", arguments.Take(4))}), код {process.ExitCode}: {error}", inputError);
        if (inputError is not null)
            throw new IOException($"Git закрыл входной канал до завершения передачи: {error}", inputError);
    }

    private static async Task<string> ReadProgressAsync(Process process, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var errors = new StringBuilder();
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            errors.AppendLine(line);
            if (errors.Length > 16000)
                errors.Remove(0, errors.Length - 16000);
            progress.Report("Git: " + line);
        }
        return errors.ToString();
    }

    private static void Stop(Process process)
    {
        if (!process.HasExited)
            process.Kill(true);
    }
}
