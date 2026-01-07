namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that tracks the current active sprite sheet for entities that support multiple sprite sheets.
/// </summary>
public struct SpriteSheetComponent
{
    /// <summary>
    ///     The sprite definition ID currently in use.
    /// </summary>
    public string CurrentSpriteSheetId { get; set; }
}
