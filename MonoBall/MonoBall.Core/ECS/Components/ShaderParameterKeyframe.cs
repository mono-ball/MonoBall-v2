namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Represents a single keyframe in a shader parameter animation timeline.
    /// </summary>
    public struct ShaderParameterKeyframe
    {
        /// <summary>
        /// Time in seconds from timeline start when this keyframe occurs.
        /// </summary>
        public float Time { get; set; }

        /// <summary>
        /// Parameter value at this keyframe.
        /// Supported types: float, Vector2, Vector3, Vector4, Color.
        /// </summary>
        public object Value { get; set; }

        /// <summary>
        /// Easing function to use when interpolating to the next keyframe.
        /// </summary>
        public EasingFunction Easing { get; set; }
    }
}
