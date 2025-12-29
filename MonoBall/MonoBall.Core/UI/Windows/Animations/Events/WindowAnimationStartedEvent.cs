using Arch.Core;

namespace MonoBall.Core.UI.Windows.Animations.Events;

/// <summary>
///     Event fired when a window animation starts.
/// </summary>
public struct WindowAnimationStartedEvent
{
    /// <summary>
    ///     Gets or sets the animation entity.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     Gets or sets the window entity being animated.
    /// </summary>
    public Entity WindowEntity { get; set; }
}
