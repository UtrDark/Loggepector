using System.Collections.Generic;
using System;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input;
using Loggepector.Models;
using Loggepector.Services;

namespace Loggepector.Views;

public sealed class LogMessageTextBlock : SelectableTextBlock
{
    public static readonly StyledProperty<string> HighlightTextProperty =
        AvaloniaProperty.Register<LogMessageTextBlock, string>(nameof(HighlightText), string.Empty);

    public string HighlightText
    {
        get => GetValue(HighlightTextProperty);
        set => SetValue(HighlightTextProperty, value);
    }

    public LogMessageTextBlock()
    {
        ActualThemeVariantChanged += (_, _) => RebuildInlines();
    }

    public bool AllowTextSelection { get; set; } = true;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EmojiService.ImagesChanged += RebuildInlines;
        RebuildInlines();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EmojiService.ImagesChanged -= RebuildInlines;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (AllowTextSelection)
            base.OnPointerPressed(e);
    }

    public static readonly StyledProperty<ICommand?> BadgeSearchCommandProperty =
        AvaloniaProperty.Register<LogMessageTextBlock, ICommand?>(nameof(BadgeSearchCommand));

    public ICommand? BadgeSearchCommand
    {
        get => GetValue(BadgeSearchCommandProperty);
        set => SetValue(BadgeSearchCommandProperty, value);
    }

    public static readonly StyledProperty<bool?> DarkThemeProperty =
        AvaloniaProperty.Register<LogMessageTextBlock, bool?>(nameof(DarkTheme));

    public bool? DarkTheme
    {
        get => GetValue(DarkThemeProperty);
        set => SetValue(DarkThemeProperty, value);
    }
    public static readonly StyledProperty<IReadOnlyList<LogMessagePart>?> PartsProperty =
        AvaloniaProperty.Register<LogMessageTextBlock, IReadOnlyList<LogMessagePart>?>(nameof(Parts));

    public IReadOnlyList<LogMessagePart>? Parts
    {
        get => GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    public static readonly StyledProperty<bool> IsTruncatedProperty =
        AvaloniaProperty.Register<LogMessageTextBlock, bool>(nameof(IsTruncated));

    public bool IsTruncated
    {
        get => GetValue(IsTruncatedProperty);
        set => SetValue(IsTruncatedProperty, value);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        SetCurrentValue(IsTruncatedProperty, TextLayout.TextLines.Any(line => line.HasCollapsed || line.HasOverflowed));
        return size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != PartsProperty && change.Property != DarkThemeProperty && change.Property != HighlightTextProperty)
            return;
        RebuildInlines();
    }

    private void RebuildInlines()
    {
        Inlines ??= new InlineCollection();
        Inlines.Clear();
        if (Parts is null)
            return;
        var ranges = new List<(int Start, int End)>();
        if (!string.IsNullOrWhiteSpace(HighlightText))
        {
            var text = string.Concat(Parts.Select(part => part.Text));
            var position = 0;
            while (position <= text.Length - HighlightText.Length)
            {
                var index = text.IndexOf(HighlightText, position, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;
                ranges.Add((index, index + HighlightText.Length));
                position = index + HighlightText.Length;
            }
        }
        var offset = 0;
        foreach (var part in Parts)
        {
            if (part.Kind == LogMessagePartKind.Emoji)
            {
                if (part.Channel is { } name && EmojiService.Images.TryGetValue(name, out var image))
                {
                    var control = new Image { Source = image, Width = 16, Height = 16 };
                    ToolTip.SetTip(control, ":" + name + ":");
                    Inlines.Add(new InlineUIContainer { BaselineAlignment = BaselineAlignment.Center, Child = control });
                }
                else
                    AddHighlightedRuns(Inlines, part.Text, offset, ranges);
                offset += part.Text.Length;
                continue;
            }
            if (part.Kind == LogMessagePartKind.Text)
            {
                AddHighlightedRuns(Inlines, part.Text, offset, ranges);
                offset += part.Text.Length;
                continue;
            }
            var style = part.Kind switch
            {
                LogMessagePartKind.CKey => LogDisplayConfig.CKeyHighlight,
                LogMessagePartKind.AI => LogDisplayConfig.AIHighlight,
                _ => LogDisplayConfig.CharacterNameHighlight
            };
            if (LogDisplayConfig.CombatHighlights.TryGetValue(part.Kind, out var combatStyle))
                style = combatStyle;
            if (part.Kind == LogMessagePartKind.Channel)
            {
                var channel = LogDisplayConfig.GetChannel(part.Channel
                    ?? throw new InvalidOperationException("Не задан канал радиоплашки."));
                style = (channel.Color, channel.Background, channel.Border);
            }
            style = LogDisplayConfig.AdaptBadge(style, DarkTheme ?? ActualThemeVariant == ThemeVariant.Dark);
            var label = new TextBlock { Foreground = style.Foreground, FontWeight = FontWeight.Normal };
            label.Inlines ??= new InlineCollection();
            AddHighlightedRuns(label.Inlines, part.Text, offset, ranges);
            offset += part.Text.Length;
            var badge = new Border
            {
                Background = style.Background,
                BorderBrush = style.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(3, 0),
                Child = label
            };
            badge.PointerPressed += (_, e) =>
            {
                var request = new BadgeSearchRequest(part, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                if ((e.KeyModifiers == KeyModifiers.Alt || e.KeyModifiers == (KeyModifiers.Alt | KeyModifiers.Shift)) &&
                    e.GetCurrentPoint(badge).Properties.IsLeftButtonPressed && BadgeSearchCommand is { } command && command.CanExecute(request))
                {
                    e.Handled = true;
                    command.Execute(request);
                }
            };
            Inlines.Add(new InlineUIContainer
            {
                BaselineAlignment = BaselineAlignment.Center,
                Child = badge
            });
        }
    }

    private static void AddHighlightedRuns(InlineCollection inlines, string text, int offset,
        IReadOnlyList<(int Start, int End)> ranges)
    {
        var position = 0;
        foreach (var range in ranges)
        {
            var start = Math.Max(0, range.Start - offset);
            var end = Math.Min(text.Length, range.End - offset);
            if (end <= start)
                continue;
            if (start > position)
                inlines.Add(new Run(text[position..start]));
            inlines.Add(new Run(text[start..end])
            {
                Background = LogDisplayConfig.FindTextBackground,
                Foreground = LogDisplayConfig.FindTextForeground
            });
            position = end;
        }
        if (position < text.Length)
            inlines.Add(new Run(text[position..]));
    }
}
