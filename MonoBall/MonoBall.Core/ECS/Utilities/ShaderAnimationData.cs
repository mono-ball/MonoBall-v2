using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Utilities
{
    /// <summary>
    /// Shared animation data struct used by multi-parameter and chain animation systems.
    /// Extracted from ShaderParameterAnimationComponent for DRY.
    /// </summary>
    public struct ShaderAnimationData
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
        /// Whether to ping-pong the animation (reverse direction when looping).
        /// </summary>
        public bool PingPong { get; set; }

        /// <summary>
        /// Creates animation data from an existing component.
        /// </summary>
        public static ShaderAnimationData FromComponent(ShaderParameterAnimationComponent component)
        {
            return new ShaderAnimationData
            {
                ParameterName = component.ParameterName,
                StartValue = component.StartValue,
                EndValue = component.EndValue,
                Duration = component.Duration,
                ElapsedTime = component.ElapsedTime,
                Easing = component.Easing,
                IsLooping = component.IsLooping,
                PingPong = component.PingPong,
            };
        }

        /// <summary>
        /// Creates a new animation data with default values.
        /// </summary>
        public static ShaderAnimationData Create(
            string parameterName,
            object startValue,
            object endValue,
            float duration,
            EasingFunction easing = EasingFunction.Linear,
            bool isLooping = false,
            bool pingPong = false
        )
        {
            return new ShaderAnimationData
            {
                ParameterName = parameterName,
                StartValue = startValue,
                EndValue = endValue,
                Duration = duration,
                ElapsedTime = 0f,
                Easing = easing,
                IsLooping = isLooping,
                PingPong = pingPong,
            };
        }
    }
}
