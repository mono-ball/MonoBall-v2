namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Blend mode for shader composition when multiple shaders are active on the same layer.
    /// All blend modes except Replace require shader-based blending with render targets.
    /// </summary>
    public enum ShaderBlendMode
    {
        /// <summary>
        /// Replace previous output (default, no blending).
        /// Shader completely replaces the previous shader's output.
        /// </summary>
        Replace,

        /// <summary>
        /// Multiply with previous output (shader-based).
        /// Result = Previous * Current
        /// Requires shader support and render targets.
        /// </summary>
        Multiply,

        /// <summary>
        /// Add to previous output (shader-based).
        /// Result = Previous + Current
        /// Requires shader support and render targets.
        /// </summary>
        Add,

        /// <summary>
        /// Overlay blend mode (shader-based).
        /// Combines previous and current with overlay algorithm.
        /// Requires shader support and render targets.
        /// </summary>
        Overlay,

        /// <summary>
        /// Screen blend mode (shader-based).
        /// Result = 1 - (1 - Previous) * (1 - Current)
        /// Requires shader support and render targets.
        /// </summary>
        Screen,
    }
}
