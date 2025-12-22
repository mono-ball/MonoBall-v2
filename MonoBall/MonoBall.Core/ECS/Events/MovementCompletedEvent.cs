using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when an entity completes movement.
    /// Published AFTER successful movement.
    /// Matches MonoBall's MovementCompletedEvent structure.
    /// </summary>
    public struct MovementCompletedEvent
    {
        /// <summary>
        /// The entity that completed movement.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// The old position (grid coordinates).
        /// </summary>
        public (int X, int Y) OldPosition { get; set; }

        /// <summary>
        /// The new position (grid coordinates).
        /// </summary>
        public (int X, int Y) NewPosition { get; set; }

        /// <summary>
        /// The movement direction.
        /// </summary>
        public Direction Direction { get; set; }

        /// <summary>
        /// The map identifier (optional).
        /// </summary>
        public string? MapId { get; set; }

        /// <summary>
        /// The time taken for movement (1.0 / MovementSpeed).
        /// </summary>
        public float MovementTime { get; set; }
    }
}
