namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Enumeration of blend modes for shader composition when multiple shaders are active.
    /// </summary>
    public enum ShaderBlendMode
    {
        /// <summary>
        /// Replace mode: Shader replaces previous output completely (no blending).
        /// </summary>
        Replace,

        /// <summary>
        /// Add mode: Shader output is added to previous output.
        /// </summary>
        Add,

        /// <summary>
        /// Multiply mode: Shader output is multiplied with previous output.
        /// </summary>
        Multiply,

        /// <summary>
        /// Overlay mode: Overlay blend of shader output with previous output.
        /// </summary>
        Overlay,

        /// <summary>
        /// Screen mode: Screen blend of shader output with previous output.
        /// </summary>
        Screen,
    }
}
