namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores tile data for a chunk.
/// </summary>
public struct TileDataComponent
{
    /// <summary>
    ///     The tileset ID used by this chunk.
    /// </summary>
    public string TilesetId { get; set; }

    /// <summary>
    ///     The tile indices array. Each element represents a tile GID (Global ID).
    /// </summary>
    public int[] TileIndices { get; set; }

    /// <summary>
    ///     The first GID (Global ID) for the tileset.
    /// </summary>
    public int FirstGid { get; set; }

    /// <summary>
    ///     Indicates whether this chunk contains any animated tiles.
    ///     Used for fast-path optimization in the renderer to avoid World.Has<> checks.
    /// </summary>
    public bool HasAnimatedTiles { get; set; }
}
