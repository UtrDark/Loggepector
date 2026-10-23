using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Loggepector.ViewModels;
using Loggepector.Utils;

namespace Loggepector.Views;

internal static class LogImageRenderer
{
    private static readonly Regex HiddenKey = new(@"\(\s*\[CKEY\]\s*\)|\[CKEY\]", RegexOptions.Compiled);
    internal static RenderTargetBitmap Render(IReadOnlyList<LogEntryViewModel> logs, bool dark, bool sanitize)
    {
        const int width = 1200;
        var foreground = Brush.Parse(dark ? "#E4E9F1" : "#202632");
        var background = Brush.Parse(dark ? "#101318" : "#FFFFFF");
        var rows = new StackPanel { Spacing = 2 };
        var keys = sanitize ? LogMessageSanitizer.CreateKeyPattern(logs.Select(log => log.Entry)) : null;
        foreach (var log in logs)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,130,Auto,*") };
            var time = new TextBlock
            {
                Text = log.TimeLabel[..^4],
                Foreground = foreground,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var type = new TextBlock
            {
                Text = log.Entry.Type,
                Foreground = log.TypeColor,
                FontWeight = FontWeight.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var message = new TextBlock
            {
                Text = sanitize ? HiddenKey.Replace(log.Entry.GetClearLogMessage(), string.Empty) : log.Message,
                Foreground = foreground,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(type, 1);
            Grid.SetColumn(message, 3);
            row.Children.Add(time);
            row.Children.Add(type);
            if (log.HasChannel)
            {
                var channel = new TextBlock
                {
                    Text = sanitize ? HiddenKey.Replace(LogMessageSanitizer.Clear(log.ChannelLabel, keys), string.Empty) : log.ChannelLabel,
                    Foreground = log.ChannelColor,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                Grid.SetColumn(channel, 2);
                row.Children.Add(channel);
            }
            row.Children.Add(message);
            rows.Children.Add(new Border
            {
                Background = log.RowBackground,
                Padding = new Thickness(12, 10),
                Child = row
            });
        }
        var image = new Border
        {
            Background = background,
            Width = width,
            Padding = new Thickness(16),
            Child = rows
        };
        TextElement.SetFontFamily(image, new FontFamily("Inter"));
        TextElement.SetFontSize(image, 14);
        image.Measure(new Size(width, double.PositiveInfinity));
        var height = Math.Ceiling(image.DesiredSize.Height);
        if (!double.IsFinite(height) || height <= 0 || height > 32767 || width * height > 32000000)
            throw new InvalidOperationException("Выбрано слишком много записей для одного PNG. Уменьшите выделение.");
        image.Arrange(new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(new PixelSize(width, (int)height), new Vector(96, 96));
        try
        {
            bitmap.Render(image);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
