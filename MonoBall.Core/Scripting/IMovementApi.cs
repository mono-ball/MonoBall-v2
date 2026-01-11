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

    /// <summary>
    ///     Gets the current movement speed for an entity (in tiles per second).
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <returns>The current movement speed in tiles per second, or null if entity doesn't have GridMovement component.</returns>
    float? GetMovementSpeed(Entity entity);

    /// <summary>
    ///     Gets the current movement type for an entity (e.g., "walk", "run", "bike").
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <returns>The current movement type, or null if entity doesn't have GridMovement component.</returns>
    string? GetMovementType(Entity entity);

    /// <summary>
    ///     Sets the movement type for an entity (e.g., "walk", "run", "bike").
    ///     Updates both MovementSpeed and CurrentMovementType in GridMovement component.
    ///     Uses the entity's sprite definition to find the movement profile.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="movementType">The movement type to set (e.g., "walk", "run", "bike"). Must match a type in the entity's movement profile.</param>
    /// <exception cref="System.ArgumentException">If entity doesn't have GridMovement or SpriteComponent.</exception>
    /// <exception cref="System.InvalidOperationException">If entity's sprite doesn't have MovementProfileId or movement type doesn't exist in profile.</exception>
    void SetMovementType(Entity entity, string movementType);

    /// <summary>
    ///     Sets the movement speed directly (in tiles per second) and updates CurrentMovementType from profile.
    ///     If speed matches a known movement type in the profile, CurrentMovementType is set accordingly.
    ///     Otherwise, CurrentMovementType is set to the default movement type.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="speed">The movement speed in tiles per second.</param>
    /// <exception cref="System.ArgumentException">If entity doesn't have GridMovement or SpriteComponent.</exception>
    /// <exception cref="System.InvalidOperationException">If entity's sprite doesn't have MovementProfileId.</exception>
    void SetMovementSpeed(Entity entity, float speed);
}
