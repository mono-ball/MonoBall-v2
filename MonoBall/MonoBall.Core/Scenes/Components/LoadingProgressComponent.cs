namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Component that tracks loading progress for the loading scene.
    /// Stores current progress percentage, current step description, and error state.
    /// </summary>
    public struct LoadingProgressComponent
    {
        /// <summary>
        /// Current loading progress as a value between 0.0 and 1.0.
        /// </summary>
        public float Progress { get; set; }

        /// <summary>
        /// Description of the current loading step.
        /// </summary>
        public string CurrentStep { get; set; }

        /// <summary>
        /// Whether loading has completed (successfully or with error).
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// Error message if loading failed, or null if successful.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
