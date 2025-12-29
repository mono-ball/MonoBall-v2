using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Null implementation of ICollisionService that allows all movement.
///     Used during development or when collision checking is disabled.
/// </summary>
/// <remarks>
///     This is a placeholder implementation. For full game behavior,
///     implement a proper CollisionService with spatial queries,
///     elevation checking, and tile behavior support.
/// </remarks>
public class NullCollisionService : ICollisionService
{
    /// <summary>
    ///     Always returns true - allows all movement without collision checking.
    /// </summary>
    public bool CanMoveTo(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection = Direction.None
    )
    {
        // No collision checking - all movement allowed
        return true;
    }

    /// <summary>
    ///     Returns default values - no jump tiles, all walkable.
    /// </summary>
    public (bool isJumpTile, Direction allowedJumpDir, bool isWalkable) GetTileCollisionInfo(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection
    )
    {
        // No collision checking - return default (no jump, walkable)
        return (false, Direction.None, true);
    }
}
