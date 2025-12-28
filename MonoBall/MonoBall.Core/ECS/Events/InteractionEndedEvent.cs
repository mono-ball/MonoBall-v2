using Arch.Core;

namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when an interaction ends (before behavior scripts are resumed).
    /// This event is fired when the message box closes and before behavior scripts are resumed.
    /// </summary>
    public struct InteractionEndedEvent
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
