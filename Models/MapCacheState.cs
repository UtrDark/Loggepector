using System.Collections.Generic;

namespace Loggepector.Models;

public sealed class MapCacheState
{
    public int SourceSchemaVersion { get; set; }
    public string? SourceCommit { get; set; }
    public Dictionary<string, string> SourceFiles { get; set; } = new();
    public Dictionary<string, MapRenderState> Maps { get; set; } = new();
}

public sealed record MapRenderState(string Fingerprint, string Directory, string[] Files);

public sealed record MapToolsSettings(bool? Enabled);

public sealed record MapChunk(string File, int Z, int Column, int Row, int Width, int Height,
    int MinX, int MaxX, int MinY, int MaxY);

public sealed record MapTileManifest(string Source, string Commit, int TileSize, int ChunkSize,
    string Origin, MapChunk[] Chunks);
