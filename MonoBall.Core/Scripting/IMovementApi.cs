using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scripting;

/// <summary>
///     API for movement-related operations.
/// </summary>
public interface IMovementApi
{
    /// <summary>
    ///     Requests movement for an entity in a specific direction.
    /// </summary>
    /// <param name="entity">The entity to move.</param>
    /// <param name="direction">The direction to move.</param>
    /// <returns>True if the movement request was created, false otherwise.</returns>
    bool RequestMovement(Entity entity, Direction direction);

    /// <summary>
    ///     Checks if an entity is currently moving.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity is moving, false otherwise.</returns>
    bool IsMoving(Entity entity);

    /// <summary>
    ///     Locks movement for an entity (prevents new movement requests).
    /// </summary>
    /// <param name="entity">The entity to lock.</param>
    void LockMovement(Entity entity);

    /// <summary>
    ///     Unlocks movement for an entity (allows new movement requests).
    /// </summary>
    /// <param name="entity">The entity to unlock.</param>
    void UnlockMovement(Entity entity);

    /// <summary>
    ///     Checks if movement is locked for an entity.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if movement is locked, false otherwise.</returns>
    bool IsMovementLocked(Entity entity);
}
