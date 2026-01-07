using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Dispatcher for tile interaction scripts.
///     Provides O(1) direct dispatch to script handlers instead of O(n) event broadcast.
/// </summary>
/// <remarks>
///     This dispatcher resolves interaction IDs to script handlers and invokes them directly.
///     It replaces the event-based approach to avoid broadcasting to all listeners.
/// </remarks>
public interface ITileInteractionDispatcher
{
    /// <summary>
    ///     Checks if a tile script blocks movement to the target position.
    /// </summary>
    /// <param name="interactionId">The interaction script ID.</param>
    /// <param name="entity">The entity attempting to move.</param>
    /// <param name="currentPosition">The entity's current tile position.</param>
    /// <param name="targetPosition">The target tile position.</param>
    /// <param name="fromDirection">The direction the entity is moving from.</param>
    /// <param name="elevation">The entity's elevation.</param>
    /// <returns>True if the script blocks movement, false otherwise.</returns>
    bool CheckCollision(
        string interactionId,
        Entity entity,
        (int X, int Y) currentPosition,
        (int X, int Y) targetPosition,
        Direction fromDirection,
        byte elevation
    );

    /// <summary>
    ///     Notifies a tile script that an entity has completed movement to a position.
    /// </summary>
    /// <param name="interactionId">The interaction script ID.</param>
    /// <param name="entity">The entity that moved.</param>
    /// <param name="newPosition">The entity's new tile position.</param>
    /// <param name="direction">The direction the entity moved.</param>
    /// <param name="elevation">The entity's elevation.</param>
    void NotifyMovementCompleted(
        string interactionId,
        Entity entity,
        (int X, int Y) newPosition,
        Direction direction,
        byte elevation
    );
}
