using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when an interaction starts (after behavior scripts are paused).
    /// This event is fired after InteractionTriggeredEvent and indicates that the interaction
    /// has been fully initialized and behavior scripts have been paused.
    /// </summary>
    public struct InteractionStartedEvent
    {
        /// <summary>
        /// Gets or sets the entity representing the interaction object.
        /// </summary>
        public Entity InteractionEntity { get; set; }

        /// <summary>
        /// Gets or sets the player entity that triggered the interaction.
        /// </summary>
        public Entity PlayerEntity { get; set; }

        /// <summary>
        /// Gets or sets the unique identifier for this interaction.
        /// </summary>
        public string InteractionId { get; set; }
    }
}
