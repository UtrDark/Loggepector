using System;
using System.Globalization;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using Loggepector.Models;
using Loggepector.Utils;

namespace Loggepector.ViewModels;

public partial class LogEntryViewModel(ILogEntry entry, DateTime roundStart, bool relative = true) : ViewModelBase
{
    public ILogEntry Entry { get; } = entry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeLabel))]
    private bool _relative = relative;

    public string TimeLabel => Relative ? _relativeTime : _serverTime;

    private readonly string _relativeTime = FormatTime(entry.Timestamp - roundStart);
    private readonly string _serverTime = entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    private static string FormatTime(TimeSpan time) => string.Create(CultureInfo.InvariantCulture,
        $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");

    public string Message { get; } = entry.GetLogMessage();

    public string RawMessage => Entry.GetRawMessage();

    private IReadOnlyList<LogMessagePart>? _messageParts;
    public IReadOnlyList<LogMessagePart> MessageParts => _messageParts ??= LogMessageHighlights.Create(Entry, Message);

    public bool IsMultiline => Message.Contains('\n') || Message.Contains('\r');

    public bool HasCoordinates => Entry.HasCoordinates;

    public bool HasChannel => Entry.Channel is not null;

    public string ChannelLabel => Entry.Channel is { } channel ? LogDisplayConfig.GetChannel(channel).Text : string.Empty;

    public IBrush ChannelBackground => Entry.Channel is { } channel
        ? LogDisplayConfig.GetChannel(channel).Background : Brushes.Transparent;

    public IBrush ChannelBorder => Entry.Channel is { } channel
        ? LogDisplayConfig.GetChannel(channel).Border : Brushes.Transparent;

    public IBrush ChannelColor => Entry.Channel is { } channel
        ? LogDisplayConfig.GetChannel(channel).Color : Brushes.Transparent;

    public IBrush TypeColor => LogDisplayConfig.GetType(Entry.Type).Foreground;

    public IBrush RowBackground => LogDisplayConfig.GetType(Entry.Type).Background;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandLabel))]
    private bool _isExpanded;

    public string ExpandLabel => IsExpanded ? "▴" : "▾";
}
