using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that applies a shader effect to a specific entity.
    /// Per-entity shaders override layer shaders when present.
    /// </summary>
    public struct ShaderComponent
    {
        /// <summary>
        /// The shader ID (e.g., "PerEntityGlow").
        /// </summary>
        public string ShaderId { get; set; }

        /// <summary>
        /// Whether the shader is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Shader parameters (parameter name -> value).
        /// Supported value types: float, Vector2, Vector3, Vector4, Color, Texture2D, Matrix.
        /// </summary>
        public Dictionary<string, object>? Parameters { get; set; }

        /// <summary>
        /// Render order for this shader (lower values render first).
        /// Used when multiple shaders are active on the same entity.
        /// </summary>
        public int RenderOrder { get; set; }
    }
}
