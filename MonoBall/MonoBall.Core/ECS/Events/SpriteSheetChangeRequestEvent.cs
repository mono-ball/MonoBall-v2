using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event published to request a sprite sheet change for an entity with SpriteSheetComponent.
/// </summary>
public struct SpriteSheetChangeRequestEvent
{
    /// <summary>
    ///     The entity that should change sprite sheet (must have SpriteSheetComponent).
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     The new sprite sheet ID to switch to.
    /// </summary>
    public string NewSpriteSheetId { get; set; }

    /// <summary>
    ///     The animation name to use in the new sprite sheet.
    /// </summary>
    public string AnimationName { get; set; }
}
