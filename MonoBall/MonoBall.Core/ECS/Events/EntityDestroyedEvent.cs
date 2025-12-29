using System;
using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when an entity is destroyed.
///     Systems that destroy entities should fire this event.
/// </summary>
public struct EntityDestroyedEvent
{
    /// <summary>
    ///     The entity that was destroyed.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     Timestamp when the entity was destroyed.
    /// </summary>
    public DateTime DestroyedAt { get; set; }
}
