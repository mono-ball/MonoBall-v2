namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Enumeration of shader layer types.
    /// </summary>
    public enum ShaderLayer
    {
        /// <summary>
        /// Shader applied to tile layer rendering.
        /// </summary>
        TileLayer,

        /// <summary>
        /// Shader applied to sprite layer rendering.
        /// </summary>
        SpriteLayer,

        /// <summary>
        /// Shader applied as post-processing to combined layers (tile + sprite).
        /// </summary>
        CombinedLayer,
    }
}
