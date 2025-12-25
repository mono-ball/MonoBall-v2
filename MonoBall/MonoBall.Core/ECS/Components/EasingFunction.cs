namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Enumeration of easing functions for shader parameter animation.
    /// </summary>
    public enum EasingFunction
    {
        /// <summary>
        /// Linear interpolation (no easing).
        /// </summary>
        Linear,

        /// <summary>
        /// Ease in (slow start, fast end).
        /// </summary>
        EaseIn,

        /// <summary>
        /// Ease out (fast start, slow end).
        /// </summary>
        EaseOut,

        /// <summary>
        /// Ease in-out (slow start and end, fast middle).
        /// </summary>
        EaseInOut,

        /// <summary>
        /// Smooth step interpolation.
        /// </summary>
        SmoothStep,
    }
}
