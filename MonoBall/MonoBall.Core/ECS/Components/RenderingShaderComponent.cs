using System.Collections.Generic;
using Arch.Core;

namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that applies a shader effect to rendering layers (tile, sprite, or combined).
///     Can be global (affects all scenes) or per-scene (affects specific scene only).
/// </summary>
public struct RenderingShaderComponent
{
    /// <summary>
    ///     The layer this shader applies to.
    /// </summary>
    public ShaderLayer Layer { get; set; }

    /// <summary>
    ///     The shader ID in mod format (e.g., "base:shader:colorgrading").
    /// </summary>
    public string ShaderId { get; set; }

    /// <summary>
    ///     Whether the shader is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    ///     Shader parameters (parameter name -> value).
    ///     Supported value types: float, Vector2, Vector3, Vector4, Color, Texture2D, Matrix.
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>
    ///     Render order for this shader (lower values render first).
    ///     Used when multiple shaders are active on the same layer.
    /// </summary>
    public int RenderOrder { get; set; }

    /// <summary>
    ///     Blend mode for shader composition when multiple shaders are active.
    ///     Replace: No blending (shader replaces previous output).
    ///     Other modes: Shader-based blending (requires render targets).
    /// </summary>
    public ShaderBlendMode BlendMode { get; set; }

    /// <summary>
    ///     Optional scene entity this shader is associated with.
    ///     If null, the shader applies globally to all scenes.
    ///     If set, the shader only applies to the specified scene.
    /// </summary>
    public Entity? SceneEntity { get; set; }
}
