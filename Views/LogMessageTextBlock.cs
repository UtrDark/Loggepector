using System.Collections.Generic;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Loggepector.Models;

namespace Loggepector.Views;

public sealed class LogMessageTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<IReadOnlyList<LogMessagePart>?> PartsProperty =
        AvaloniaProperty.Register<LogMessageTextBlock, IReadOnlyList<LogMessagePart>?>(nameof(Parts));

    public IReadOnlyList<LogMessagePart>? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != PartsProperty)
            return;
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        if (Parts is null)
            return;
        foreach (var part in Parts)
        {
            if (part.Kind == LogMessagePartKind.Text)
            {
                Inlines.Add(new Run(part.Text));
                continue;
            }
            var style = part.Kind switch
            {
                LogMessagePartKind.CKey => LogDisplayConfig.CKeyHighlight,
                LogMessagePartKind.AI => LogDisplayConfig.AIHighlight,
                _ => LogDisplayConfig.CharacterNameHighlight
            };
            if (part.Kind == LogMessagePartKind.Channel)
            {
                var channel = LogDisplayConfig.GetChannel(part.Channel
                    ?? throw new InvalidOperationException("Не задан канал радиоплашки."));
                style = (channel.Color, channel.Background, channel.Border);
            }
            Inlines.Add(new InlineUIContainer
            {
                BaselineAlignment = BaselineAlignment.Center,
                Child = new Border
                {
                    Background = style.Background,
                    BorderBrush = style.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(3, 0),
                    Child = new TextBlock { Text = part.Text, Foreground = style.Foreground, FontWeight = FontWeight.Normal }
                }
            });
        }
    }
}
