using Porycon3.Models;

namespace Porycon3.Services;

/// <summary>
/// Builds optimized tilesets by collecting used tiles and creating
/// a deduplicated tileset image with proper GID mapping.
///
/// Phase 1: Placeholder implementation
/// Phase 2+: Full implementation with ImageSharp
/// </summary>
public class TilesetBuilder
{
    private readonly Dictionary<TileKey, int> _tileMapping = new();
    private int _nextGid = 1;

    public record TileKey(int TileId, int PaletteIndex, bool FlipH, bool FlipV);

    /// <summary>
    /// Gets or creates a GID for a tile.
    /// </summary>
    public int GetOrCreateGid(TileData tile)
    {
        var key = new TileKey(tile.TileId, tile.PaletteIndex, tile.FlipHorizontal, tile.FlipVertical);

        if (!_tileMapping.TryGetValue(key, out var gid))
        {
            gid = _nextGid++;
            _tileMapping[key] = gid;
        }

        return gid;
    }

    /// <summary>
    /// Get the tile mapping for serialization.
    /// </summary>
    public IReadOnlyDictionary<TileKey, int> GetMapping() => _tileMapping;

    /// <summary>
    /// Reset the builder for a new tileset.
    /// </summary>
    public void Reset()
    {
        _tileMapping.Clear();
        _nextGid = 1;
    }
}
