namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that animates shader parameters over time.
    /// Can be attached to entities with ShaderComponent or LayerShaderComponent.
    /// </summary>
    public struct ShaderParameterAnimationComponent
    {
        /// <summary>
        /// The name of the shader parameter to animate.
        /// </summary>
        public string ParameterName { get; set; }

        /// <summary>
        /// The starting value for the animation.
        /// </summary>
        public object StartValue { get; set; }

        /// <summary>
        /// The ending value for the animation.
        /// </summary>
        public object EndValue { get; set; }

        /// <summary>
        /// The duration of the animation in seconds.
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        /// The elapsed time since animation started (in seconds).
        /// </summary>
        public float ElapsedTime { get; set; }

        /// <summary>
        /// The easing function to use for interpolation.
        /// </summary>
        public EasingFunction Easing { get; set; }

        /// <summary>
        /// Whether the animation loops (restarts when complete).
        /// </summary>
        public bool IsLooping { get; set; }

        /// <summary>
        /// Whether the animation is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Whether to ping-pong the animation (reverse direction when looping).
        /// </summary>
        public bool PingPong { get; set; }
    }
}
