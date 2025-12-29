namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that defines an entity's interaction properties.
///     Used by NPCs and other entities that can be interacted with by the player.
/// </summary>
public struct InteractionComponent
{
    /// <summary>
    ///     Gets or sets the unique identifier for this interaction.
    ///     Typically matches the entity's ID (e.g., NPC ID).
    /// </summary>
    public string InteractionId { get; set; }

    /// <summary>
    ///     Gets or sets the script definition ID that handles this interaction.
    ///     References a ScriptDefinition that contains the interaction script.
    /// </summary>
    public string ScriptDefinitionId { get; set; }

    /// <summary>
    ///     Gets or sets the width of the interaction area in tiles.
    ///     Default: 1 tile.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    ///     Gets or sets the height of the interaction area in tiles.
    ///     Default: 1 tile.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    ///     Gets or sets the elevation (z-order) required for interaction.
    ///     Player and interaction entity must be on the same elevation.
    /// </summary>
    public int Elevation { get; set; }

    /// <summary>
    ///     Gets or sets the required facing direction for interaction (null if any direction is allowed).
    ///     If set, the player must be facing this direction to interact.
    /// </summary>
    public Direction? RequiredFacing { get; set; }

    /// <summary>
    ///     Gets or sets whether this interaction is currently enabled.
    ///     When false, the interaction cannot be triggered.
    /// </summary>
    public bool IsEnabled { get; set; }
}
