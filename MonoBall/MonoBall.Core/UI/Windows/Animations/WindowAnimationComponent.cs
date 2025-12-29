using Arch.Core;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.UI.Windows.Animations;

/// <summary>
///     Component that tracks window animation state and timing.
///     Attached to entities that have animated windows.
/// </summary>
public struct WindowAnimationComponent
{
    /// <summary>
    ///     Gets or sets the current animation state.
    /// </summary>
    public WindowAnimationState State { get; set; }

    /// <summary>
    ///     Gets or sets the time elapsed in the current animation state.
    /// </summary>
    public float ElapsedTime { get; set; }

    /// <summary>
    ///     Gets or sets the animation configuration.
    ///     Defines animation type, durations, easing, and parameters.
    /// </summary>
    public WindowAnimationConfig Config { get; set; }

    /// <summary>
    ///     Gets or sets the current animated position offset (X, Y).
    ///     Applied to window position during rendering.
    /// </summary>
    public Vector2 PositionOffset { get; set; }

    /// <summary>
    ///     Gets or sets the current animated scale factor.
    ///     Applied to window size during rendering (1.0 = normal size).
    /// </summary>
    public float Scale { get; set; }

    /// <summary>
    ///     Gets or sets the current animated opacity (0.0 = transparent, 1.0 = opaque).
    ///     Applied to window rendering.
    /// </summary>
    public float Opacity { get; set; }

    /// <summary>
    ///     Gets or sets the window entity this animation applies to.
    ///     Used to reference the window being animated.
    ///     Must be validated (World.IsAlive) before use if window entity may be destroyed.
    /// </summary>
    public Entity WindowEntity { get; set; }
}
