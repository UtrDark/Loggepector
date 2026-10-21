using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Loggepector.Views;

internal sealed record MapTileDraw(Bitmap Bitmap, Rect Source, Rect Destination);

internal sealed class MapLayerControl : Control
{
    internal IReadOnlyList<MapTileDraw> Tiles { get; set; } = new List<MapTileDraw>();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        using (context.PushClip(new Rect(Bounds.Size)))
            foreach (var tile in Tiles)
                context.DrawImage(tile.Bitmap, tile.Source, tile.Destination);
    }
}
