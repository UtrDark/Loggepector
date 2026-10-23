using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Loggepector.Views;

public sealed class FindOverview : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> PositionsProperty =
        AvaloniaProperty.Register<FindOverview, IReadOnlyList<double>?>(nameof(Positions));
    public static readonly StyledProperty<double> CurrentPositionProperty =
        AvaloniaProperty.Register<FindOverview, double>(nameof(CurrentPosition), -1);

    static FindOverview() => AffectsRender<FindOverview>(PositionsProperty, CurrentPositionProperty);

    public IReadOnlyList<double>? Positions
    {
        get => GetValue(PositionsProperty);
        set => SetValue(PositionsProperty, value);
    }

    public double CurrentPosition
    {
        get => GetValue(CurrentPositionProperty);
        set => SetValue(CurrentPositionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Positions is null || Bounds.Height <= 0)
            return;
        var height = Math.Max(0, Bounds.Height - 4);
        var previous = -1;
        foreach (var position in Positions)
        {
            var y = (int)(position * height);
            if (y == previous)
                continue;
            context.DrawRectangle(LogDisplayConfig.FindMarkerColor, null, new Rect(0, y, Bounds.Width, 2));
            previous = y;
        }
        if (CurrentPosition >= 0)
            context.DrawRectangle(LogDisplayConfig.CurrentFindMarkerColor, null,
                new Rect(0, CurrentPosition * height, Bounds.Width, 4));
    }
}
