namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Current state of a window animation.
    /// </summary>
    public enum WindowAnimationState
    {
        /// <summary>
        /// Animation is not started yet.
        /// </summary>
        NotStarted,

        /// <summary>
        /// Animation is currently playing.
        /// </summary>
        Playing,

        /// <summary>
        /// Animation is paused (can be resumed).
        /// </summary>
        Paused,

        /// <summary>
        /// Animation has completed.
        /// </summary>
        Completed,
    }
}
