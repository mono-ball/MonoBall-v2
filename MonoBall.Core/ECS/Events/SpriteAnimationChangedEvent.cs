using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a sprite's animation changes (works for both NPCs and Players).
/// </summary>
public struct SpriteAnimationChangedEvent
{
    /// <summary>
    ///     The entity reference for the sprite.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     The name of the previous animation.
    /// </summary>
    public string OldAnimationName { get; set; }

    /// <summary>
    ///     The name of the new animation.
    /// </summary>
    public string NewAnimationName { get; set; }
}
