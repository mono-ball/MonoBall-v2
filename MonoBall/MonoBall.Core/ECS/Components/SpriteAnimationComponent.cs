namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores animation state for a sprite.
///     Contains animation name, timing, and playback state.
///     Requires SpriteComponent to be present (updates SpriteComponent.frameIndex).
///     Matches oldmonoball Animation component structure for proper turn-in-place behavior.
/// </summary>
public struct SpriteAnimationComponent
{
    /// <summary>
    ///     The name of the current animation.
    /// </summary>
    public string CurrentAnimationName { get; set; }

    /// <summary>
    ///     The current frame index in the animation sequence (0-based).
    ///     Used internally by SpriteAnimationSystem to track animation progress.
    ///     SpriteAnimationSystem updates SpriteComponent.FrameIndex based on this.
    /// </summary>
    public int CurrentAnimationFrameIndex { get; set; }

    /// <summary>
    ///     Time elapsed on the current frame in seconds.
    /// </summary>
    public float ElapsedTime { get; set; }

    /// <summary>
    ///     Whether the animation is currently playing.
    /// </summary>
    public bool IsPlaying { get; set; }

    /// <summary>
    ///     Whether the animation has completed (for non-looping animations or PlayOnce).
    ///     Used for turn-in-place detection - when IsComplete is true, the turn animation finished.
    /// </summary>
    public bool IsComplete { get; set; }

    /// <summary>
    ///     Whether the animation should play only once regardless of manifest Loop setting.
    ///     When true, the animation will set IsComplete=true after one full cycle.
    ///     Used for turn-in-place animations (Pokemon Emerald WALK_IN_PLACE_FAST behavior).
    /// </summary>
    public bool PlayOnce { get; set; }

    /// <summary>
    ///     Bit field of frame indices that have already triggered their events.
    ///     Used to prevent re-triggering events when frame hasn't changed.
    ///     Reset when animation changes or loops.
    ///     Each bit represents a frame index (supports up to 64 frames).
    ///     Zero-allocation alternative to HashSet.
    /// </summary>
    public ulong TriggeredEventFrames { get; set; }
}
