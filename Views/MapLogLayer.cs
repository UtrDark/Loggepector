using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Loggepector.Models;

namespace Loggepector.Views;

internal sealed record MapLogDraw(MapLogPoint Log, Point Position, FormattedText Text, Pen Ring, Pen Halo, IBrush Fill, Geometry? Arc);

internal sealed class MapLogLayer : Control
{
    private const double TileHalfSize = 16;
    private const double HaloWidth = 7;
    private const double Radius = TileHalfSize - HaloWidth / 2;
    private static readonly Dictionary<string, Color> PlayerColors = new(StringComparer.Ordinal);
    private MapLogDraw[] _draws = Array.Empty<MapLogDraw>();
    private double _zoom = 1;

    public MapLogLayer()
    {
        ActualThemeVariantChanged += (_, _) => RefreshAppearance();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextElement.ForegroundProperty)
            RefreshAppearance();
    }

    private void RefreshAppearance()
    {
        if (_draws is not { Length: > 0 })
            return;
        var previous = _draws;
        var positions = previous.ToDictionary(draw => draw.Log, draw => draw.Position);
        _draws = Array.Empty<MapLogDraw>();
        Update(previous.Select(draw => draw.Log).ToArray(), point => positions[point], _zoom);
    }

    internal void Update(MapLogPoint[] points, Func<MapLogPoint, Point> project, double zoom)
    {
        _zoom = zoom;
        var previous = _draws.ToDictionary(draw => draw.Log);
        _draws = points.Select(point =>
        {
            if (previous.TryGetValue(point, out var draw))
                return draw with { Position = project(point) };
            var color = Color.Parse("#707D91");
            if (point.PlayerKey.Length > 0 && !PlayerColors.TryGetValue(point.PlayerKey, out color))
            {
                color = new HsvColor(1, Random.Shared.NextDouble() * 360, 0.85, 0.95).ToRgb();
                PlayerColors.Add(point.PlayerKey, color);
            }
            var brush = new SolidColorBrush(color);
            var typeColor = ((SolidColorBrush)LogDisplayConfig.GetType(point.Entries[^1].Type).Foreground).Color;
            var surface = ActualThemeVariant == ThemeVariant.Dark ? Color.Parse("#14181F") : Colors.White;
            var fill = new SolidColorBrush(Color.FromRgb((byte)((typeColor.R + surface.R) / 2),
                (byte)((typeColor.G + surface.G) / 2), (byte)((typeColor.B + surface.B) / 2)));
            var text = new FormattedText(point.Name, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), 7, TextElement.GetForeground(this))
            {
                MaxTextWidth = 28,
                MaxTextHeight = 20,
                TextAlignment = TextAlignment.Center,
                Trimming = TextTrimming.CharacterEllipsis
            };
            return new MapLogDraw(point, project(point), text, new Pen(brush, 3),
                new Pen(new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B)), HaloWidth),
                fill, CreateArc(point.Progress));
        }).ToArray();
        InvalidateVisual();
    }

    private static Geometry? CreateArc(double progress)
    {
        if (progress <= 0)
            return null;
        if (progress >= 1)
            return new EllipseGeometry(new Rect(-Radius, -Radius, Radius * 2, Radius * 2));
        var angle = progress * Math.PI * 2;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(0, -Radius), false);
            path.ArcTo(new Point(Math.Sin(angle) * Radius, -Math.Cos(angle) * Radius),
                new Size(Radius, Radius), 0, progress > 0.5, SweepDirection.Clockwise);
            path.EndFigure(false);
        }
        return geometry;
    }

    internal MapLogPoint? Hit(Point point)
    {
        MapLogDraw? nearest = null;
        var distance = Math.Pow(TileHalfSize * _zoom, 2);
        foreach (var draw in _draws)
        {
            var dx = point.X - draw.Position.X;
            var dy = point.Y - draw.Position.Y;
            var squared = dx * dx + dy * dy;
            if (squared <= distance)
            {
                distance = squared;
                nearest = draw;
            }
        }
        return nearest?.Log;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using (context.PushClip(new Rect(Bounds.Size)))
            foreach (var draw in _draws)
                using (context.PushTransform(new Matrix(_zoom, 0, 0, _zoom, draw.Position.X, draw.Position.Y)))
                {
                    context.DrawEllipse(draw.Fill, null, default, Radius - 1.5, Radius - 1.5);
                    context.DrawEllipse(null, draw.Halo, default, Radius, Radius);
                    if (draw.Arc is not null)
                        context.DrawGeometry(null, draw.Ring, draw.Arc);
                    context.DrawText(draw.Text, new Point(-14, -draw.Text.Height / 2));
                }
    }
}
