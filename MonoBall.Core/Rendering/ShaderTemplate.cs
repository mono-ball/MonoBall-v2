using System.Collections.Generic;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Represents a pre-configured combination of shaders that can be applied to a layer.
///     Templates define multiple shaders with their blend modes, render orders, and parameters.
/// </summary>
public class ShaderTemplate
{
    /// <summary>
    ///     Gets or sets the unique identifier for this template (e.g., "base:template:nighttime").
    /// </summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name of this template.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the description of what this template does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    ///     Gets or sets the list of shaders in this template, in render order.
    /// </summary>
    public List<ShaderTemplateEntry> Shaders { get; set; } = new();
}

/// <summary>
///     Represents a single shader entry within a shader template.
/// </summary>
public class ShaderTemplateEntry
{
    /// <summary>
    ///     Gets or sets the shader ID in mod format (e.g., "base:shader:bloom").
    /// </summary>
    public string ShaderId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the blend mode for this shader when composing with previous shaders.
    /// </summary>
    public ShaderBlendMode BlendMode { get; set; } = ShaderBlendMode.Replace;

    /// <summary>
    ///     Gets or sets the render order for this shader (lower values render first).
    /// </summary>
    public int RenderOrder { get; set; }

    /// <summary>
    ///     Gets or sets the shader parameters (parameter name -> value).
    ///     Supported value types: float, Vector2, Vector3, Vector4, Color, Texture2D, Matrix.
    /// </summary>
    public Dictionary<string, object>? Parameters { get; set; }
}
