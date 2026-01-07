using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Interface for shader template operations.
///     Applies pre-configured shader combinations to layers.
/// </summary>
public interface IShaderTemplateSystem
{
    /// <summary>
    ///     Applies a shader template to the specified layer.
    /// </summary>
    /// <param name="templateId">The template ID to apply.</param>
    /// <param name="layer">The layer to apply the template to.</param>
    /// <returns>True if the template was successfully applied.</returns>
    bool ApplyTemplate(string templateId, ShaderLayer layer);

    /// <summary>
    ///     Removes all shaders from a layer (clears the layer's shader stack).
    /// </summary>
    /// <param name="layer">The layer to clear.</param>
    void ClearLayer(ShaderLayer layer);
}
