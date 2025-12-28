using Arch.Core;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that tracks an active interaction state on the player entity.
    /// Added to the player entity when an interaction starts, removed when it ends.
    /// Used to resume behavior scripts when message boxes close.
    /// </summary>
    public struct InteractionStateComponent
    {
        /// <summary>
        /// Gets or sets the behavior definition ID that was paused for this interaction.
        /// Used to look up BehaviorDefinition to get ScriptId for resuming the behavior script.
        /// </summary>
        public string BehaviorId { get; set; }

        /// <summary>
        /// Gets or sets the interaction entity (NPC or map interaction).
        /// </summary>
        public Entity InteractionEntity { get; set; }

        /// <summary>
        /// Gets or sets the player entity that triggered this interaction.
        /// </summary>
        public Entity PlayerEntity { get; set; }
    }
}
