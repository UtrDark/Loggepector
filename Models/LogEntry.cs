using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using Loggepector.Utils;

namespace Loggepector.Models;

public record LogEntry(DateTime Timestamp, string Type, string Message, int LineNumber,
    string? Channel = null) : ILogEntry
{
    private static readonly Regex LongLine = new(
        @"[^\r\n]{120}(?=[^\r\n])", RegexOptions.Compiled);

    private static readonly Regex Location = new(
        @"\([^()\r\n]+?\(\s*(?<x>-?[0-9]+)\s*,\s*(?<y>-?[0-9]+)\s*,\s*(?<z>-?[0-9]+)\s*\)\s*\)",
        RegexOptions.Compiled);

    private string? _formattedMessage;
    private string? _logMessage;
    private IReadOnlyList<LogMessagePart>? _searchBadges;
    private IReadOnlyList<LogMessagePart>? _parts;
    private IReadOnlyList<LogMessagePart>? _partsWithArea;
    private string? _findText;
    private string? _findTextWithArea;

    internal IReadOnlyList<LogMessagePart> GetMessageParts(bool showArea) => showArea
        ? _partsWithArea ??= LogMessageHighlights.Create(this, GetLogMessage(), true)
        : _parts ??= LogMessageHighlights.Create(this, GetLogMessage());

    internal string GetFindText(bool showArea) => showArea
        ? _findTextWithArea ??= string.Concat(GetMessageParts(true).Select(part => part.Text))
        : _findText ??= string.Concat(GetMessageParts(false).Select(part => part.Text));

    internal IReadOnlyList<LogMessagePart> GetSearchBadges() => _searchBadges ??=
        GetMessageParts(true)
            .Where(part => part.Kind != LogMessagePartKind.Text).ToArray();
    private string? _coordinatesSource;
    private (int X, int Y, int Z)? _coordinates;

    public string RawMessage { get; init; } = Message;

    internal Regex? SensitiveKeys { get; init; }
    internal IReadOnlySet<string>? CharacterNames { get; init; }

    public bool HasCoordinates => GetCoordinates() is not null;

    public (int X, int Y, int Z)? GetCoordinates()
    {
        var rawMessage = GetRawMessage();
        if (ReferenceEquals(_coordinatesSource, rawMessage))
            return _coordinates;

        var match = Location.Match(rawMessage);
        _coordinates = match.Success
            ? (int.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["z"].Value, CultureInfo.InvariantCulture))
            : null;
        _coordinatesSource = rawMessage;
        return _coordinates;
    }

    public string GetRawMessage() => RawMessage;

    public string GetLogMessage() => _logMessage ??= LongLine.Replace(GetFormattedMessage(), "$0\n");

    public string GetClearLogMessage() => LongLine.Replace(
        LogMessageSanitizer.Clear(GetFormattedMessage(), SensitiveKeys ?? LogMessageSanitizer.CreateKeyPattern(new[] { this })),
        "$0\n");

    private string GetFormattedMessage() => _formattedMessage ??= FormatMessage();

    protected virtual string FormatMessage() => Message;
}
