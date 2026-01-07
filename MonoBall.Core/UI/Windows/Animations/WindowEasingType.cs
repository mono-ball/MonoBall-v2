namespace MonoBall.Core.UI.Windows.Animations;

/// <summary>
///     Easing functions for window animations.
/// </summary>
public enum WindowEasingType
{
    /// <summary>
    ///     Linear interpolation (no easing).
    /// </summary>
    Linear,

    /// <summary>
    ///     Ease in (slow start, fast end).
    /// </summary>
    EaseIn,

    /// <summary>
    ///     Ease out (fast start, slow end).
    /// </summary>
    EaseOut,

    /// <summary>
    ///     Ease in-out (slow start and end, fast middle).
    /// </summary>
    EaseInOut,

    /// <summary>
    ///     Cubic ease in.
    /// </summary>
    EaseInCubic,

    /// <summary>
    ///     Cubic ease out.
    /// </summary>
    EaseOutCubic,

    /// <summary>
    ///     Cubic ease in-out.
    /// </summary>
    EaseInOutCubic,
}
