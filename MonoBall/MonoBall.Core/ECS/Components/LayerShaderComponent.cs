using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that applies a shader effect to an entire layer (tile, sprite, or combined).
    /// Attach to a singleton entity to control layer-wide shader effects.
    /// </summary>
    public struct LayerShaderComponent
    {
        /// <summary>
        /// The layer this shader applies to.
        /// </summary>
        public ShaderLayer Layer { get; set; }

        /// <summary>
        /// The shader ID in mod format (e.g., "base:shader:colorgrading").
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
        /// Used when multiple shaders are active on the same layer.
        /// </summary>
        public int RenderOrder { get; set; }

        /// <summary>
        /// Blend mode for shader composition when multiple shaders are active.
        /// Replace: No blending (shader replaces previous output).
        /// Other modes: Shader-based blending (requires render targets).
        /// </summary>
        public ShaderBlendMode BlendMode { get; set; }
    }
}
