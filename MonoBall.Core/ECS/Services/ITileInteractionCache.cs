namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Cache for tile interaction script IDs.
///     Maps tile positions to their associated interaction scripts for O(1) dispatch.
/// </summary>
/// <remarks>
///     This cache is populated by MapLoaderSystem when loading tile data.
///     It is intentionally decoupled from MapDefinition to follow Dependency Inversion.
/// </remarks>
public interface ITileInteractionCache
{
    /// <summary>
    ///     Gets the interaction ID for a tile at the specified position.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="x">The X coordinate in tile space.</param>
    /// <param name="y">The Y coordinate in tile space.</param>
    /// <param name="elevation">The elevation level.</param>
    /// <returns>The interaction ID, or null if no interaction is assigned.</returns>
    string? GetTileInteractionId(string mapId, int x, int y, int elevation);

    /// <summary>
    ///     Sets the interaction ID for a tile at the specified position.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="x">The X coordinate in tile space.</param>
    /// <param name="y">The Y coordinate in tile space.</param>
    /// <param name="elevation">The elevation level.</param>
    /// <param name="interactionId">The interaction ID, or null to remove.</param>
    void SetTileInteraction(string mapId, int x, int y, int elevation, string? interactionId);

    /// <summary>
    ///     Removes all interaction data for a map.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    void UnloadMap(string mapId);
}
