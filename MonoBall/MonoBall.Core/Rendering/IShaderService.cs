using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for loading and managing shader effects.
    /// </summary>
    public interface IShaderService
    {
        /// <summary>
        /// Loads a shader effect from mods. Shader ID must be in format "{namespace}:shader:{name}" (all lowercase).
        /// Returns null if the shader cannot be loaded (consistent with other resource loaders).
        /// </summary>
        /// <param name="shaderId">The shader ID (e.g., "base:shader:colorgrading").</param>
        /// <returns>The loaded Effect, or null if loading failed.</returns>
        Effect? LoadShader(string shaderId);

        /// <summary>
        /// Gets a cached shader effect, or loads it if not cached. Loads from mods via LoadShader().
        /// Returns null if the shader cannot be loaded (consistent with other resource loaders).
        /// </summary>
        /// <param name="shaderId">The shader ID (e.g., "base:shader:colorgrading").</param>
        /// <returns>The Effect, or null if loading failed.</returns>
        Effect? GetShader(string shaderId);

        /// <summary>
        /// Checks if a shader exists and is loaded.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>True if the shader exists and is loaded.</returns>
        bool HasShader(string shaderId);

        /// <summary>
        /// Unloads a shader from cache.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        void UnloadShader(string shaderId);

        /// <summary>
        /// Unloads all shaders from cache.
        /// </summary>
        void UnloadAllShaders();
    }
}
