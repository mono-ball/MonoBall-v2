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
    ///     Checks if a tile position is walkable without triggering script interactions.
    ///     Use this for pathfinding, AI planning, or any query where you don't want side effects.
    /// </summary>
    /// <param name="entity">The entity attempting to move.</param>
    /// <param name="targetX">The X coordinate in tile space.</param>
    /// <param name="targetY">The Y coordinate in tile space.</param>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="fromDirection">Optional direction moving FROM (for behavior checking).</param>
    /// <returns>True if the position is walkable from this direction, false if blocked.</returns>
    bool CanMoveToSilent(
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

    /// <summary>
    ///     Resolves a movement request, handling cross-map transitions.
    ///     Returns the actual target map and grid coordinates for the movement.
    /// </summary>
    /// <param name="entity">The entity attempting to move.</param>
    /// <param name="targetX">The target X coordinate in source map's tile space.</param>
    /// <param name="targetY">The target Y coordinate in source map's tile space.</param>
    /// <param name="sourceMapId">The source map identifier.</param>
    /// <param name="fromDirection">Direction moving FROM (for behavior blocking).</param>
    /// <returns>
    ///     MovementResolution containing:
    ///     - CanMove: Whether the movement is allowed
    ///     - TargetMapId: The map the entity will be in after moving (may differ from source for cross-map)
    ///     - TargetX: The grid X coordinate on the target map
    ///     - TargetY: The grid Y coordinate on the target map
    ///     - IsCrossMapMovement: Whether this movement crosses a map boundary
    /// </returns>
    MovementResolution ResolveMovement(
        Entity entity,
        int targetX,
        int targetY,
        string? sourceMapId,
        Direction fromDirection = Direction.None
    );
}

/// <summary>
///     Result of resolving a movement request.
/// </summary>
public readonly struct MovementResolution
{
    /// <summary>
    ///     Whether the movement is allowed.
    /// </summary>
    public bool CanMove { get; init; }

    /// <summary>
    ///     The map the entity will be in after moving.
    ///     May differ from source map for cross-map movement.
    /// </summary>
    public string? TargetMapId { get; init; }

    /// <summary>
    ///     The grid X coordinate on the target map.
    /// </summary>
    public int TargetX { get; init; }

    /// <summary>
    ///     The grid Y coordinate on the target map.
    /// </summary>
    public int TargetY { get; init; }

    /// <summary>
    ///     The world pixel X coordinate of the target position.
    /// </summary>
    public float TargetPixelX { get; init; }

    /// <summary>
    ///     The world pixel Y coordinate of the target position.
    /// </summary>
    public float TargetPixelY { get; init; }

    /// <summary>
    ///     Whether this movement crosses a map boundary.
    /// </summary>
    public bool IsCrossMapMovement { get; init; }

    /// <summary>
    ///     Creates a blocked movement resolution.
    /// </summary>
    public static MovementResolution Blocked => new() { CanMove = false };

    /// <summary>
    ///     Creates a successful same-map movement resolution.
    /// </summary>
    public static MovementResolution Success(
        string mapId,
        int x,
        int y,
        float pixelX,
        float pixelY
    ) =>
        new()
        {
            CanMove = true,
            TargetMapId = mapId,
            TargetX = x,
            TargetY = y,
            TargetPixelX = pixelX,
            TargetPixelY = pixelY,
            IsCrossMapMovement = false,
        };

    /// <summary>
    ///     Creates a successful cross-map movement resolution.
    /// </summary>
    public static MovementResolution CrossMap(
        string targetMapId,
        int x,
        int y,
        float pixelX,
        float pixelY
    ) =>
        new()
        {
            CanMove = true,
            TargetMapId = targetMapId,
            TargetX = x,
            TargetY = y,
            TargetPixelX = pixelX,
            TargetPixelY = pixelY,
            IsCrossMapMovement = true,
        };
}
