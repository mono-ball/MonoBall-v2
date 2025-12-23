namespace MonoBall.Core.Scenes.Events
{
    /// <summary>
    /// Event fired when loading completes (successfully or with error).
    /// </summary>
    public struct LoadingCompleteEvent
    {
        /// <summary>
        /// Whether loading completed successfully.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if loading failed, or null if successful.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }
}
