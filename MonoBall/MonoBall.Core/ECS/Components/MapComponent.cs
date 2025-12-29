namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores map definition ID and metadata.
/// </summary>
public struct MapComponent
{
    /// <summary>
    ///     The map definition ID.
    /// </summary>
    public string MapId { get; set; }

    /// <summary>
    ///     The map width in tiles.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    ///     The map height in tiles.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    ///     The tile width in pixels.
    /// </summary>
    public int TileWidth { get; set; }

    /// <summary>
    ///     The tile height in pixels.
    /// </summary>
    public int TileHeight { get; set; }
}
