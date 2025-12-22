using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when movement is blocked.
    /// Matches MonoBall's MovementBlockedEvent structure.
    /// </summary>
    public struct MovementBlockedEvent
    {
        /// <summary>
        /// The entity whose movement was blocked.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// The reason for blocking (e.g., "Collision", "Out of bounds", "Cancelled by event handler").
        /// </summary>
        public string BlockReason { get; set; }

        /// <summary>
        /// The target position that was blocked (grid coordinates).
        /// </summary>
        public (int X, int Y) TargetPosition { get; set; }

        /// <summary>
        /// The movement direction.
        /// </summary>
        public Direction Direction { get; set; }

        /// <summary>
        /// The map identifier (optional).
        /// </summary>
        public string? MapId { get; set; }
    }
}
