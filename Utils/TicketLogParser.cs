using System;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loggepector.Models;

namespace Loggepector.Utils;

internal sealed class TicketLogParser
{
    private int _nextNumber = 1;
    private (string Key, string? Type, string? Message)? _lastCreation;
    private static readonly Regex Number = new(@"\b(?:Ticket|Тикет)\s*#\d+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Create = new(@"^[ \t]*(?:-[ \t]*)?Action: create_ticket[ \t]+(?<json>\{[^\r\n]*\})[ \t\r]*$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Actor = new(@"^(?<key>[^\s]+) \(as (?<name>.*?) at -?\d+,\s*-?\d+,\s*-?\d+\)", RegexOptions.Compiled);

    public ILogEntry? Parse(ILogEntry entry)
    {
        var source = (LogEntry)entry;
        if (entry.Type == "MISC" && source.Message.Contains("create_ticket", StringComparison.Ordinal) &&
            Create.Match(source.Message) is { Success: true } create)
        {
            using var document = JsonDocument.Parse(create.Groups["json"].Value);
            var message = document.RootElement.GetProperty("message").GetString();
            var type = document.RootElement.GetProperty("ticketType").GetString();
            var actor = Actor.Match(source.Message);
            if (!actor.Success)
                throw new JsonException("В записи create_ticket отсутствует автор тикета.");
            var creation = (CKey.Normalize(actor.Groups["key"].Value), type, message);
            if (_lastCreation == creation)
                return null;
            _lastCreation = creation;
            var identity = actor.Groups["key"].Value + " " + actor.Groups["name"].Value;
            var number = _nextNumber++;
            var text = $"Ticket #{number.ToString(CultureInfo.InvariantCulture)}: {WebUtility.HtmlEncode(identity)} created ({WebUtility.HtmlEncode(type)}): {WebUtility.HtmlEncode(message)}";
            return new AdminPrivateLogEntry(source with { Type = "TICKETS", Message = text });
        }
        if (entry is AdminPrivateLogEntry && Number.IsMatch(entry.GetLogMessage()))
            return ((LogEntry)entry) with { Type = "TICKETS" };
        return entry;
    }
}
