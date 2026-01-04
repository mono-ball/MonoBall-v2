namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores sprite rendering data.
///     Contains sprite ID, current frame index, and flip flags.
///     All sprite entities must have this component.
///     For animated sprites, SpriteAnimationSystem updates FrameIndex when animation advances.
/// </summary>
public struct SpriteComponent
{
    /// <summary>
    ///     The sprite definition ID to render.
    ///     Cannot be null or empty. Validated at render time (throws InvalidOperationException if sprite not found).
    /// </summary>
    public string SpriteId { get; set; }

    /// <summary>
    ///     The current frame index to render (0-based index into sprite sheet frames).
    ///     For static sprites: Set manually and remains constant.
    ///     For animated sprites: Updated by SpriteAnimationSystem based on current animation frame.
    ///     Must be non-negative and within sprite frame count. Validated at render time (throws ArgumentOutOfRangeException if out of range).
    /// </summary>
    public int FrameIndex { get; set; }

    /// <summary>
    ///     Whether to flip the sprite horizontally.
    ///     For animated sprites: Updated by SpriteAnimationSystem from animation manifest.
    /// </summary>
    public bool FlipHorizontal { get; set; }

    /// <summary>
    ///     Whether to flip the sprite vertically.
    ///     For animated sprites: Updated by SpriteAnimationSystem from animation manifest.
    /// </summary>
    public bool FlipVertical { get; set; }
}
