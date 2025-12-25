namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that defines a timeline for animating shader parameters with keyframes.
    /// Keyframes are stored in ShaderParameterTimelineSystem (not in component).
    /// </summary>
    public struct ShaderParameterTimelineComponent
    {
        /// <summary>
        /// The name of the shader parameter to animate.
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// The elapsed time since timeline started (in seconds).
        /// </summary>
        public float ElapsedTime { get; set; }

        /// <summary>
        /// Whether the timeline loops (restarts when complete).
        /// </summary>
        public bool IsLooping { get; set; }

        /// <summary>
        /// Whether the timeline is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Total timeline duration in seconds (calculated from keyframes).
        /// Set by ShaderParameterTimelineSystem when keyframes are added.
        /// </summary>
        public float Duration { get; set; }
    }
}
