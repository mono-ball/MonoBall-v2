using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a player interacts with an entity that has an InteractionComponent.
///     Scripts attached to the interaction entity can subscribe to this event to handle the interaction.
/// </summary>
public struct InteractionTriggeredEvent
{
    /// <summary>
    ///     Gets or sets the entity representing the interaction object (NPC, sign, etc.).
    /// </summary>
    public Entity InteractionEntity { get; set; }

    /// <summary>
    ///     Gets or sets the player entity that triggered the interaction.
    /// </summary>
    public Entity PlayerEntity { get; set; }

    /// <summary>
    ///     Gets or sets the unique identifier for this interaction.
    /// </summary>
    public string InteractionId { get; set; }

    /// <summary>
    ///     Gets or sets the script definition ID that should handle this interaction.
    /// </summary>
    public string? ScriptDefinitionId { get; set; }
}
