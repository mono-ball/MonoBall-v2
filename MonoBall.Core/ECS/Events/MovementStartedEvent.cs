using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when an entity starts moving.
///     Published BEFORE movement validation, allowing handlers to cancel movement.
///     Matches MonoBall's MovementStartedEvent structure.
/// </summary>
public struct MovementStartedEvent
{
    /// <summary>
    ///     The entity starting movement.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     The start position in pixels.
    /// </summary>
    public Vector2 StartPosition { get; set; }

    /// <summary>
    ///     The target position in pixels.
    /// </summary>
    public Vector2 TargetPosition { get; set; }

    /// <summary>
    ///     The movement direction.
    /// </summary>
    public Direction Direction { get; set; }

    /// <summary>
    ///     Whether this event has been cancelled.
    ///     Handlers can set this to true to prevent movement.
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    ///     Reason for cancellation (if cancelled).
    /// </summary>
    public string? CancellationReason { get; set; }
}
