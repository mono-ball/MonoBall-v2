namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Enumeration of popup animation states.
    /// </summary>
    public enum PopupAnimationState
    {
        /// <summary>
        /// Popup is sliding down from above the screen.
        /// </summary>
        SlidingDown,

        /// <summary>
        /// Popup is visible and paused (waiting before sliding up).
        /// </summary>
        Paused,

        /// <summary>
        /// Popup is sliding back up above the screen.
        /// </summary>
        SlidingUp,
    }

    /// <summary>
    /// Component that tracks popup animation state and timing.
    /// </summary>
    public struct PopupAnimationComponent
    {
        /// <summary>
        /// Gets or sets the current animation state.
        /// </summary>
        public PopupAnimationState State { get; set; }

        /// <summary>
        /// Gets or sets the time elapsed in the current animation state.
        /// </summary>
        public float ElapsedTime { get; set; }

        /// <summary>
        /// Gets or sets the duration for the slide down animation in seconds (default: 0.3s).
        /// </summary>
        public float SlideDownDuration { get; set; }

        /// <summary>
        /// Gets or sets the duration to pause when visible in seconds (default: 2.0s).
        /// </summary>
        public float PauseDuration { get; set; }

        /// <summary>
        /// Gets or sets the duration for the slide up animation in seconds (default: 0.3s).
        /// </summary>
        public float SlideUpDuration { get; set; }

        /// <summary>
        /// Gets or sets the height of the popup in pixels (calculated from text + padding).
        /// </summary>
        public float PopupHeight { get; set; }

        /// <summary>
        /// Gets or sets the current Y position in world space.
        /// 0 = visible at top of screen, negative = above screen.
        /// </summary>
        public float CurrentY { get; set; }
    }
}
