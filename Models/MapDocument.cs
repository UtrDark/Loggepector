using System.Linq;

namespace Loggepector.Models;

public sealed record MapViewTile(string Path, MapChunk Chunk);

public sealed record MapDocument(string Name, int TileSize, int ChunkSize, int DefaultLayer, MapViewTile[] Tiles,
    int WorldZOffset)
{
    public int Width { get; } = Tiles.Max(tile => tile.Chunk.Column * ChunkSize + tile.Chunk.Width);
    public int Height { get; } = Tiles.Max(tile => tile.Chunk.Row * ChunkSize + tile.Chunk.Height);
    public int[] Layers { get; } = Tiles.Select(tile => tile.Chunk.Z).Distinct().OrderBy(z => z).ToArray();
}
