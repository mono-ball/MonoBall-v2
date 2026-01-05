namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Cache for per-elevation tile collision data.
///     Provides O(1) lookups for collision values from map collision layers.
/// </summary>
public interface ICollisionLayerCache
{
    /// <summary>
    ///     Gets the collision value for a tile at a specific elevation.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="x">The X coordinate in tile space.</param>
    /// <param name="y">The Y coordinate in tile space.</param>
    /// <param name="elevation">The elevation level to check.</param>
    /// <returns>The collision value (0=passable, 1+=blocking), or null if out of bounds or no collision layer for this elevation.</returns>
    byte? GetCollisionValue(string mapId, int x, int y, int elevation);

    /// <summary>
    ///     Checks if a tile position is within map bounds.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="x">The X coordinate in tile space.</param>
    /// <param name="y">The Y coordinate in tile space.</param>
    /// <returns>True if position is within bounds, false otherwise.</returns>
    bool IsInBounds(string mapId, int x, int y);

    /// <summary>
    ///     Checks if collision data is loaded for a map.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <returns>True if the map has collision data loaded.</returns>
    bool HasMapData(string mapId);

    /// <summary>
    ///     Loads collision data for a specific elevation layer.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="elevation">The elevation level for this collision layer.</param>
    /// <param name="collisionData">The collision data (1 byte per tile).</param>
    /// <param name="width">The layer width in tiles.</param>
    /// <param name="height">The layer height in tiles.</param>
    /// <param name="offsetX">The X offset of this layer in tile space (default 0).</param>
    /// <param name="offsetY">The Y offset of this layer in tile space (default 0).</param>
    void LoadCollisionLayer(
        string mapId,
        int elevation,
        byte[] collisionData,
        int width,
        int height,
        int offsetX = 0,
        int offsetY = 0
    );

    /// <summary>
    ///     Sets the map dimensions (for bounds checking when no collision layers exist).
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="width">The map width in tiles.</param>
    /// <param name="height">The map height in tiles.</param>
    void SetMapDimensions(string mapId, int width, int height);

    /// <summary>
    ///     Unloads collision data for a map when it is unloaded.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    void UnloadMap(string mapId);
}
