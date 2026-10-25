using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Loggepector.Models;

namespace Loggepector;

public static class LogDisplayConfig
{
    public static IBrush FindTextBackground { get; } = Brush.Parse("#FFD54F");
    public static IBrush FindTextForeground { get; } = Brush.Parse("#211900");
    public static IBrush FindMarkerColor { get; } = Brush.Parse("#D6A500");
    public static IBrush CurrentFindMarkerColor { get; } = Brush.Parse("#EF7D24");

    public static IBrush AlternateRowOverlay { get; } = Brush.Parse("#28000000");
    public static double LightBadgeTextScale { get; } = 0.5;

    public static (IBrush Foreground, IBrush Background, IBrush Border) AdaptBadge(
        (IBrush Foreground, IBrush Background, IBrush Border) style, bool dark)
    {
        if (dark)
            return style;
        var color = ((ISolidColorBrush)style.Foreground).Color;
        var text = Color.FromRgb((byte)(color.R * LightBadgeTextScale), (byte)(color.G * LightBadgeTextScale),
            (byte)(color.B * LightBadgeTextScale));
        return (new SolidColorBrush(text), style.Background,
            new SolidColorBrush(Color.FromArgb(128, text.R, text.G, text.B)));
    }

    public static IReadOnlyDictionary<LogMessagePartKind, (IBrush Foreground, IBrush Background, IBrush Border)> CombatHighlights { get; } =
        new Dictionary<LogMessagePartKind, (IBrush, IBrush, IBrush)>
        {
            [LogMessagePartKind.Action] = CreateBadgeStyle("#CD6464"),
            [LogMessagePartKind.Item] = CreateBadgeStyle("#D2AB62"),
            [LogMessagePartKind.Help] = CreateBadgeStyle("#4C9A63"),
            [LogMessagePartKind.Harm] = CreateBadgeStyle("#DD5353"),
            [LogMessagePartKind.Brute] = CreateBadgeStyle("#DD5353"),
            [LogMessagePartKind.Burn] = CreateBadgeStyle("#E99A45"),
            [LogMessagePartKind.Tox] = CreateBadgeStyle("#6CA729"),
            [LogMessagePartKind.Oxy] = CreateBadgeStyle("#57B8F0"),
            [LogMessagePartKind.Health] = CreateBadgeStyle("#61B390"),
            [LogMessagePartKind.DamageType] = CreateBadgeStyle("#CD6464"),
            [LogMessagePartKind.Area] = CreateBadgeStyle("#829BAE"),
            [LogMessagePartKind.Ticket] = CreateBadgeStyle("#D2AB62")
        };

    private static (IBrush Foreground, IBrush Background, IBrush Border) CreateBadgeStyle(string value)
    {
        var color = Color.Parse(value);
        return (new SolidColorBrush(color), new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B)),
            new SolidColorBrush(Color.FromArgb(128, color.R, color.G, color.B)));
    }

    public static (IBrush Foreground, IBrush Background, IBrush Border) AIHighlight { get; } =
        (Brush.Parse("#E99A45"), Brush.Parse("#30E58A2B"), Brush.Parse("#A0E58A2B"));

    public static (IBrush Foreground, IBrush Background, IBrush Border) CKeyHighlight { get; } =
        (Brush.Parse("#6AADE0"), Brush.Parse("#20468CC6"), Brush.Parse("#80468CC6"));

    public static (IBrush Foreground, IBrush Background, IBrush Border) CharacterNameHighlight { get; } =
        (Brush.Parse("#B393D0"), Brush.Parse("#209B78B5"), Brush.Parse("#809B78B5"));

    public static IReadOnlyDictionary<string, (string Text, IBrush Color, IBrush Background, IBrush Border)> Channels { get; } =
        new Dictionary<string, (string, IBrush, IBrush, IBrush)>(StringComparer.Ordinal)
        {
            ["121.1"] = CreateChannelStyle("Аплинк", "#8f4a4b"),
            ["121.3"] = CreateChannelStyle("Синдикат", "#8f4a4b"),
            ["121.5"] = CreateChannelStyle("Красные", "#ff0000"),
            ["121.7"] = CreateChannelStyle("Синие", "#0000ff"),
            ["121.9"] = CreateChannelStyle("Зеленые", "#00ff00"),
            ["122.1"] = CreateChannelStyle("Желтые", "#d1ba22"),
            ["133.7"] = CreateChannelStyle("ЦК", "#2681a5"),
            ["134.7"] = CreateChannelStyle("Снабжение", "#b88646"),
            ["134.9"] = CreateChannelStyle("Обслуживание", "#6ca729"),
            ["135.1"] = CreateChannelStyle("Исследование", "#c68cfa"),
            ["135.3"] = CreateChannelStyle("Командование", "#fcdf03"),
            ["135.5"] = CreateChannelStyle("Медицина", "#57b8f0"),
            ["135.7"] = CreateChannelStyle("Инженерия", "#f37746"),
            ["135.9"] = CreateChannelStyle("Безопасность", "#dd3535"),
            ["136.1"] = CreateChannelStyle("Юридический", "#df3e71"),
            ["141.5"] = CreateChannelStyle("Развлечение", "#79c5a8"),
            ["144.7"] = CreateChannelStyle("ИИ (личный)", "#d65d95"),
            ["145.9"] = CreateChannelStyle("Общий", "#1ecc43")
        };

    public static IReadOnlyDictionary<string, (IBrush Foreground, IBrush Background)> Types { get; } =
        new Dictionary<string, (IBrush, IBrush)>(StringComparer.Ordinal)
        {
            ["SAY"] = CreateStyle("#4C9A63"),
            ["ANOUNCE"] = CreateStyle("#B68A36"),
            ["EXPLOSION"] = CreateStyle("#E08036"),
            ["AI"] = CreateStyle("#E99A45"),
            ["RADIO"] = CreateStyle("#468CC6"),
            ["MISC"] = CreateStyle("#9B78B5"),
            ["ACCESS"] = CreateStyle("#B68A36"),
            ["ATTACK"] = CreateStyle("#CD6464"),
            ["ADMIN"] = CreateStyle("#B85B9D"),
            ["TICKETS"] = CreateStyle("#D2AB62"),
            ["FORBIDDEN"] = CreateStyle("#E08036"),
            ["EMOTE"] = CreateStyle("#459C9C")
        };

    private static readonly (IBrush Foreground, IBrush Background)[] Palette =
        new[] { "#4C9A63", "#468CC6", "#9B78B5", "#B68A36", "#CD6464", "#B85B9D", "#459C9C" }
            .Select(CreateStyle).ToArray();

    public static (string Text, IBrush Color, IBrush Background, IBrush Border) GetChannel(string channel)
    {
        if (Channels.TryGetValue(channel, out var style))
            return style;
        var palette = GetPaletteStyle(channel);
        return (channel, palette.Foreground, palette.Background, palette.Foreground);
    }

    private static (string Text, IBrush Color, IBrush Background, IBrush Border) CreateChannelStyle(string text, string value)
    {
        var color = Color.Parse(value);
        return (text, new SolidColorBrush(color), new SolidColorBrush(Color.FromArgb(32, color.R, color.G, color.B)),
            new SolidColorBrush(Color.FromArgb(128, color.R, color.G, color.B)));
    }

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
