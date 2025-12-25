namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that defines a timeline for animating shader parameters using keyframes.
    /// Keyframes are stored externally in ShaderParameterTimelineSystem.
    /// </summary>
    public struct ShaderParameterTimelineComponent
    {
        /// <summary>
        /// Gets or sets the name of the shader parameter to animate.
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// Gets or sets the duration of the timeline in seconds.
        /// Calculated automatically from keyframes, but can be set manually.
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        /// Gets or sets the elapsed time since the timeline started (in seconds).
        /// </summary>
        public float ElapsedTime { get; set; }

        /// <summary>
        /// Gets or sets whether the timeline loops (restarts when complete).
        /// </summary>
        public bool IsLooping { get; set; }

        /// <summary>
        /// Gets or sets whether the timeline is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }
    }
}
