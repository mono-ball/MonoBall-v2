using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for tile-based collision detection.
///     Provides collision queries without per-frame updates.
/// </summary>
/// <remarks>
///     This is a service, not a system. It doesn't run every frame;
///     instead, it provides on-demand collision checking when other
///     systems need to validate movement or check tile properties.
///     Matches oldmonoball ICollisionService interface.
/// </remarks>
public interface ICollisionService
{
    /// <summary>
    ///     Checks if a tile position is walkable (not blocked by collision).
    /// </summary>
    /// <param name="entity">The entity attempting to move.</param>
    /// <param name="targetX">The X coordinate in tile space.</param>
    /// <param name="targetY">The Y coordinate in tile space.</param>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="fromDirection">Optional direction moving FROM (for behavior checking).</param>
    /// <returns>True if the position is walkable from this direction, false if blocked.</returns>
    bool CanMoveTo(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection = Direction.None
    );

    /// <summary>
    ///     Optimized method that queries collision data for a tile position ONCE.
    ///     Eliminates redundant spatial hash queries by returning all collision info in a single call.
    /// </summary>
    /// <param name="entity">The entity attempting to move.</param>
    /// <param name="targetX">The X coordinate in tile space.</param>
    /// <param name="targetY">The Y coordinate in tile space.</param>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="fromDirection">Direction moving FROM (for behavior blocking).</param>
    /// <returns>
    ///     Tuple containing:
    ///     - isJumpTile: Whether the tile contains a jump behavior
    ///     - allowedJumpDir: The direction you can jump (or None)
    ///     - isWalkable: Whether the position is walkable from the given direction
    /// </returns>
    (bool isJumpTile, Direction allowedJumpDir, bool isWalkable) GetTileCollisionInfo(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection
    );
}
