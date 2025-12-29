using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Maps;

/// <summary>
///     Represents an individual tile definition within a tileset.
/// </summary>
public class TilesetTile
{
    /// <summary>
    ///     The local tile ID within the tileset (0-based).
    /// </summary>
    [JsonPropertyName("localTileId")]
    public int LocalTileId { get; set; }

    /// <summary>
    ///     The type of the tile (nullable).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    ///     The tile behavior ID (nullable).
    /// </summary>
    [JsonPropertyName("tileBehaviorId")]
    public string? TileBehaviorId { get; set; }

    /// <summary>
    ///     Animation frames for this tile (nullable).
    /// </summary>
    [JsonPropertyName("animation")]
    public List<TileAnimationFrame>? Animation { get; set; }
}
