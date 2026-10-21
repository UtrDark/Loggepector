using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Loggepector.Models;

namespace Loggepector.Views;

public sealed class MapViewport : Panel
{
    public static readonly StyledProperty<MapDocument?> DocumentProperty =
        AvaloniaProperty.Register<MapViewport, MapDocument?>(nameof(Document));
    public static readonly StyledProperty<int> SelectedLayerProperty =
        AvaloniaProperty.Register<MapViewport, int>(nameof(SelectedLayer), 1);
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<MapViewport, double>(nameof(Zoom), 0.125);

    private const long CacheBudget = 192L * 1024 * 1024;
    private readonly Dictionary<TileKey, CachedTile> _cache = new();
    private readonly Dictionary<int, MapLayerControl> _layers = new();
    private readonly CancellationTokenSource _lifetime = new();
    private MapViewTile[] _visible = Array.Empty<MapViewTile>();
    private readonly HashSet<TileKey> _drawn = new();
    private Vector _offset;
    private Point? _dragPoint;
    private int _resolution = 128;
    private long _clock;
    private bool _loading;
    private bool _loadFailed;
    private bool _fitted;

    public event Action<string>? LoadFailed;

    public MapDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public int SelectedLayer
    {
        get => GetValue(SelectedLayerProperty);
        set => SetValue(SelectedLayerProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public MapViewport()
    {
        ClipToBounds = true;
        Background = Brush.Parse("#E3E7EC");
        Cursor = new Cursor(StandardCursorType.Hand);
        DoubleTapped += (_, _) => Fit();
        SizeChanged += (_, _) =>
        {
            if (!_fitted)
                Fit();
            else
                Refresh();
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var layer in _layers.Values)
            layer.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var layer in _layers.Values)
            layer.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            Children.Clear();
            _layers.Clear();
            _loadFailed = false;
            _fitted = false;
            if (Document is { } document)
                foreach (var z in document.Layers)
                {
                    var layer = new MapLayerControl { IsHitTestVisible = false };
                    _layers.Add(z, layer);
                    Children.Add(layer);
                }
            Fit();
            Refresh();
        }
        else if (change.Property == SelectedLayerProperty || change.Property == ZoomProperty)
            Refresh();
    }

    public void Fit()
    {
        if (Document is not { } map || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var zoom = Math.Min(4, Math.Min(Bounds.Width / map.Width, Bounds.Height / map.Height));
        _offset = new Vector((Bounds.Width - map.Width * zoom) / 2, (Bounds.Height - map.Height * zoom) / 2);
        _fitted = true;
        SetCurrentValue(ZoomProperty, zoom);
        Refresh();
    }

    private void ZoomAt(Point point, double factor)
    {
        if (Document is not { } map || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var minimum = Math.Min(4, Math.Min(Bounds.Width / map.Width, Bounds.Height / map.Height));
        var zoom = Math.Clamp(Zoom * factor, minimum, 4);
        var world = (point - _offset) / Zoom;
        _offset = new Vector(point.X - world.X * zoom, point.Y - world.Y * zoom);
        SetCurrentValue(ZoomProperty, zoom);
        Refresh();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        _dragPoint = e.GetPosition(this);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragPoint is not { } previous)
            return;
        var point = e.GetPosition(this);
        _offset += point - previous;
        _dragPoint = point;
        Refresh();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragPoint = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragPoint = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), Math.Pow(1.2, e.Delta.Y));
        e.Handled = true;
    }

    private void Refresh()
    {
        if (_lifetime.IsCancellationRequested)
            return;
        if (Document is not { } map || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _visible = Array.Empty<MapViewTile>();
            UpdateLayers();
            return;
        }
        var minimum = Math.Min(4, Math.Min(Bounds.Width / map.Width, Bounds.Height / map.Height));
        if (Zoom < minimum)
        {
            ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), minimum / Zoom);
            return;
        }
        var width = map.Width * Zoom;
        var height = map.Height * Zoom;
        _offset = new Vector(
            width <= Bounds.Width ? (Bounds.Width - width) / 2 : Math.Clamp(_offset.X, Bounds.Width - width, 0),
            height <= Bounds.Height ? (Bounds.Height - height) / 2 : Math.Clamp(_offset.Y, Bounds.Height - height, 0));
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        _resolution = 64;
        while (_resolution < map.ChunkSize && _resolution < map.ChunkSize * Zoom * scaling)
            _resolution *= 2;
        var visible = new Rect(-_offset.X / Zoom, -_offset.Y / Zoom, Bounds.Width / Zoom, Bounds.Height / Zoom)
            .Inflate(8 / Zoom);
        _visible = map.Tiles.Where(tile => tile.Chunk.Z <= SelectedLayer &&
            new Rect(tile.Chunk.Column * map.ChunkSize, tile.Chunk.Row * map.ChunkSize,
                tile.Chunk.Width, tile.Chunk.Height).Intersects(visible))
            .OrderByDescending(tile => tile.Chunk.Z)
            .ThenBy(tile => Math.Abs((tile.Chunk.Column + 0.5) * map.ChunkSize - visible.Center.X) +
                            Math.Abs((tile.Chunk.Row + 0.5) * map.ChunkSize - visible.Center.Y)).ToArray();
        UpdateLayers();
        TrimCache();
        if (!_loading && !_loadFailed)
            _ = LoadVisibleAsync();
    }

    private void UpdateLayers()
    {
        _drawn.Clear();
        if (Document is not { } map)
            return;
        foreach (var (z, layer) in _layers)
        {
            var draws = new List<MapTileDraw>();
            foreach (var tile in _visible.Where(tile => tile.Chunk.Z == z))
            {
                var key = new TileKey(tile.Path, _resolution);
                if (!_cache.TryGetValue(key, out var cached))
                {
                    var other = _cache.FirstOrDefault(pair => pair.Key.Path == tile.Path);
                    if (other.Value is null)
                        continue;
                    key = other.Key;
                    cached = other.Value;
                }
                cached.LastUsed = ++_clock;
                _drawn.Add(key);
                var size = cached.Bitmap.Size;
                draws.Add(new MapTileDraw(cached.Bitmap,
                    new Rect(0, 0, size.Width * tile.Chunk.Width / map.ChunkSize,
                        size.Height * tile.Chunk.Height / map.ChunkSize),
                    new Rect(_offset.X + tile.Chunk.Column * map.ChunkSize * Zoom,
                        _offset.Y + tile.Chunk.Row * map.ChunkSize * Zoom,
                        tile.Chunk.Width * Zoom, tile.Chunk.Height * Zoom)));
            }
            layer.Tiles = draws;
            layer.IsVisible = z <= SelectedLayer;
            layer.Opacity = z == SelectedLayer ? 1 : 0.55;
            var radius = z == SelectedLayer ? 0 : 2.5;
            if (radius == 0)
                layer.Effect = null;
            else if (layer.Effect is not BlurEffect)
                layer.Effect = new BlurEffect { Radius = radius };
            layer.InvalidateVisual();
        }
    }

    private async Task LoadVisibleAsync()
    {
        _loading = true;
        try
        {
            while (!_lifetime.IsCancellationRequested && !_loadFailed)
            {
                var missing = _visible.Select(tile => new TileKey(tile.Path, _resolution))
                    .Where(key => !_cache.ContainsKey(key)).Distinct().Take(4).ToArray();
                if (missing.Length == 0)
                    break;
                var tasks = missing.Select(key => Task.Run(() =>
                {
                    _lifetime.Token.ThrowIfCancellationRequested();
                    using var stream = File.OpenRead(key.Path);
                    return (Key: key, Bitmap: Bitmap.DecodeToWidth(stream, key.Resolution));
                }, _lifetime.Token)).ToArray();
                (TileKey Key, Bitmap Bitmap)[] loaded;
                try
                {
                    loaded = await Task.WhenAll(tasks);
                }
                catch
                {
                    foreach (var task in tasks.Where(task => task.Status == TaskStatus.RanToCompletion))
                        task.Result.Bitmap.Dispose();
                    throw;
                }
                foreach (var tile in loaded)
                {
                    if (_lifetime.IsCancellationRequested)
                        tile.Bitmap.Dispose();
                    else
                        _cache.Add(tile.Key, new CachedTile(tile.Bitmap, ++_clock));
                }
                if (!_lifetime.IsCancellationRequested)
                {
                    UpdateLayers();
                    TrimCache();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _loadFailed = true;
            LoadFailed?.Invoke($"Ошибка загрузки чанков: {exception.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    private void TrimCache()
    {
        var bytes = _cache.Values.Sum(tile => tile.Bytes);
        foreach (var (key, tile) in _cache.Where(pair => !_drawn.Contains(pair.Key))
                     .OrderBy(pair => pair.Value.LastUsed).ToArray())
        {
            if (bytes <= CacheBudget)
                break;
            _cache.Remove(key);
            bytes -= tile.Bytes;
            tile.Bitmap.Dispose();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _lifetime.Cancel();
        foreach (var tile in _cache.Values)
            tile.Bitmap.Dispose();
        _cache.Clear();
        Children.Clear();
        _layers.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private readonly record struct TileKey(string Path, int Resolution);

    private sealed class CachedTile(Bitmap bitmap, long lastUsed)
    {
        internal Bitmap Bitmap { get; } = bitmap;
        internal long LastUsed { get; set; } = lastUsed;
        internal long Bytes { get; } = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
    }
}
