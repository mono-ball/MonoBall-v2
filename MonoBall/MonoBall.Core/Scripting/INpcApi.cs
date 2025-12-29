using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scripting;

/// <summary>
///     API for NPC-related operations.
///     Provides NPC-specific functionality like facing direction and movement state.
/// </summary>
public interface INpcApi
{
    /// <summary>
    ///     Sets an NPC's facing direction without moving.
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <param name="direction">Direction to face.</param>
    void FaceDirection(Entity npc, Direction direction);

    /// <summary>
    ///     Gets an NPC's current facing direction.
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <returns>The facing direction, or null if NPC doesn't have GridMovement component.</returns>
    Direction? GetFacingDirection(Entity npc);

    /// <summary>
    ///     Makes an NPC face toward another entity (e.g., face the player).
    ///     Calculates direction based on positions.
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <param name="target">The entity to face toward.</param>
    void FaceEntity(Entity npc, Entity target);

    /// <summary>
    ///     Gets an NPC's current grid position.
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <returns>The position component, or null if not found.</returns>
    PositionComponent? GetPosition(Entity npc);

    /// <summary>
    ///     Sets an NPC's movement state (running state).
    /// </summary>
    /// <param name="npc">The NPC entity.</param>
    /// <param name="state">The running state to set.</param>
    void SetMovementState(Entity npc, RunningState state);
}
