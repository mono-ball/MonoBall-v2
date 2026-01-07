namespace MonoBall.Core.UI.Windows.Animations;

/// <summary>
///     Types of window animations supported.
/// </summary>
public enum WindowAnimationType
{
    /// <summary>
    ///     No animation (instant transition).
    /// </summary>
    None,

    /// <summary>
    ///     Slide animation (position change).
    /// </summary>
    Slide,

    /// <summary>
    ///     Fade animation (opacity change).
    /// </summary>
    Fade,

    /// <summary>
    ///     Scale animation (size change).
    /// </summary>
    Scale,

    /// <summary>
    ///     Combined slide and fade animation.
    /// </summary>
    SlideFade,

    /// <summary>
    ///     Combined slide and scale animation.
    /// </summary>
    SlideScale,

    /// <summary>
    ///     Pause/hold at current state (no animation, just wait).
    /// </summary>
    Pause,
}
