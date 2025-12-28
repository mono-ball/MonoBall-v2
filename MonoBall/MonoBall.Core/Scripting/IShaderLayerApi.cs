using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scripting
{
    /// <summary>
    /// Layer-level (global/screen) shader control API.
    /// Provides methods to add, remove, and manage shaders on rendering layers.
    /// Split from IShaderApi per Interface Segregation Principle (ISP).
    /// </summary>
    public interface IShaderLayerApi
    {
        /// <summary>
        /// Adds a shader to a rendering layer.
        /// Creates a new entity with RenderingShaderComponent.
        /// </summary>
        /// <param name="layer">The layer to add the shader to.</param>
        /// <param name="shaderId">The shader ID in mod format (e.g., "base:shader:colorgrading").</param>
        /// <param name="renderOrder">The render order (lower values render first). Default is 0.</param>
        /// <returns>The created shader entity, or null if shader definition not found.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown if shaderId is null.</exception>
        Entity? AddLayerShader(ShaderLayer layer, string shaderId, int renderOrder = 0);

        /// <summary>
        /// Removes a layer shader entity.
        /// </summary>
        /// <param name="shaderEntity">The shader entity to remove.</param>
        /// <exception cref="System.ArgumentException">Thrown if entity is not alive.</exception>
        void RemoveLayerShader(Entity shaderEntity);

        /// <summary>
        /// Enables a layer shader by ID.
        /// </summary>
        /// <param name="layer">The layer the shader is on.</param>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>True if shader was found and enabled, false otherwise.</returns>
        bool EnableLayerShader(ShaderLayer layer, string shaderId);

        /// <summary>
        /// Disables a layer shader by ID.
        /// </summary>
        /// <param name="layer">The layer the shader is on.</param>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>True if shader was found and disabled, false otherwise.</returns>
        bool DisableLayerShader(ShaderLayer layer, string shaderId);

        /// <summary>
        /// Sets a parameter on a layer shader by ID.
        /// </summary>
        /// <param name="layer">The layer the shader is on.</param>
        /// <param name="shaderId">The shader ID.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <param name="value">The value to set.</param>
        /// <returns>True if shader was found and parameter set, false otherwise.</returns>
        bool SetLayerParameter(ShaderLayer layer, string shaderId, string paramName, object value);

        /// <summary>
        /// Gets a parameter from a layer shader by ID.
        /// </summary>
        /// <param name="layer">The layer the shader is on.</param>
        /// <param name="shaderId">The shader ID.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <returns>The parameter value, or null if not found.</returns>
        object? GetLayerParameter(ShaderLayer layer, string shaderId, string paramName);

        /// <summary>
        /// Finds a layer shader entity by layer and shader ID.
        /// </summary>
        /// <param name="layer">The layer to search.</param>
        /// <param name="shaderId">The shader ID to find.</param>
        /// <returns>The shader entity, or null if not found.</returns>
        Entity? FindLayerShader(ShaderLayer layer, string shaderId);

        /// <summary>
        /// Gets all shader entities on a layer.
        /// </summary>
        /// <param name="layer">The layer to query.</param>
        /// <returns>Collection of shader entities on the layer.</returns>
        System.Collections.Generic.IEnumerable<Entity> GetLayerShaders(ShaderLayer layer);
    }
}
