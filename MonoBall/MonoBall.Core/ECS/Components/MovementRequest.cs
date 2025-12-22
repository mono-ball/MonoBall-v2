namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component representing a pending movement request.
    /// InputSystem creates these, MovementSystem validates and executes them.
    /// Uses component pooling - the component stays on the entity and is marked
    /// as inactive instead of being removed. This avoids expensive ECS structural changes.
    /// Matches MonoBall's MovementRequest component structure.
    /// </summary>
    public struct MovementRequest
    {
        /// <summary>
        /// Gets or sets the requested movement direction.
        /// </summary>
        public Direction Direction { get; set; }

        /// <summary>
        /// Gets or sets whether this request is active and pending processing.
        /// When false, the request has been processed and is waiting to be reused.
        /// This replaces component removal to avoid expensive archetype transitions.
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// Initializes a new instance of the MovementRequest struct.
        /// </summary>
        /// <param name="direction">The requested movement direction.</param>
        /// <param name="active">Whether the request is active (default: true).</param>
        public MovementRequest(Direction direction, bool active = true)
        {
            Direction = direction;
            Active = active;
        }
    }
}
