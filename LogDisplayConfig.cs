using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Loggepector;

public static class LogDisplayConfig
{
    public static IReadOnlyDictionary<string, (string Text, IBrush Color)> Channels { get; } =
        new Dictionary<string, (string, IBrush)>(StringComparer.Ordinal)
        {
            ["145.9"] = ("145.9", Brush.Parse("#4C9A63"))
        };

    public static IReadOnlyDictionary<string, (IBrush Foreground, IBrush Background)> Types { get; } =
        new Dictionary<string, (IBrush, IBrush)>(StringComparer.Ordinal)
        {
            ["SAY"] = CreateStyle("#4C9A63"),
            ["RADIO"] = CreateStyle("#468CC6"),
            ["MISC"] = CreateStyle("#9B78B5"),
            ["ACCESS"] = CreateStyle("#B68A36"),
            ["ATTACK"] = CreateStyle("#CD6464"),
            ["ADMIN"] = CreateStyle("#B85B9D"),
            ["EMOTE"] = CreateStyle("#459C9C")
        };

    private static readonly (IBrush Foreground, IBrush Background)[] Palette =
        new[] { "#4C9A63", "#468CC6", "#9B78B5", "#B68A36", "#CD6464", "#B85B9D", "#459C9C" }
            .Select(CreateStyle).ToArray();

    public static (string Text, IBrush Color) GetChannel(string channel) =>
        Channels.TryGetValue(channel, out var style) ? style : (channel, GetPaletteStyle(channel).Foreground);

    public static (IBrush Foreground, IBrush Background) GetType(string type) =>
        Types.TryGetValue(type, out var style) ? style : GetPaletteStyle(type);

    private static (IBrush Foreground, IBrush Background) CreateStyle(string value)
    {
        var color = Color.Parse(value);
        return (new SolidColorBrush(color), new SolidColorBrush(Color.FromArgb(24, color.R, color.G, color.B)));
    }

    private static (IBrush Foreground, IBrush Background) GetPaletteStyle(string value)
    {
        var hash = 0u;
        foreach (var character in value)
            hash = unchecked(hash * 31 + character);
        return Palette[hash % (uint)Palette.Length];
    }
}
