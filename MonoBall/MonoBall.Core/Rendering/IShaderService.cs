using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for loading and managing shader effects.
    /// </summary>
    public interface IShaderService
    {
        /// <summary>
        /// Loads a shader effect from mods.
        /// Throws exceptions on failure (fail fast per .cursorrules).
        /// Use this method when shader loading failures should cause immediate errors.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>The loaded Effect.</returns>
        /// <exception cref="ArgumentException">Thrown when shaderId is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when shader definition is not found, mod manifest is missing, or shader loading fails.</exception>
        /// <exception cref="FileNotFoundException">Thrown when compiled shader file is not found.</exception>
        Effect LoadShader(string shaderId);

        /// <summary>
        /// Gets a cached shader effect, or loads it if not cached. Loads from mods via LoadShader().
        /// Throws exceptions on failure (fail fast per .cursorrules).
        /// Use this method when you want to load a shader and have failures propagate immediately.
        /// For optional shaders, check HasShader() first before calling this method.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>The Effect.</returns>
        /// <exception cref="ArgumentException">Thrown when shaderId is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when shader definition is not found, mod manifest is missing, or shader loading fails.</exception>
        /// <exception cref="FileNotFoundException">Thrown when compiled shader file is not found.</exception>
        Effect GetShader(string shaderId);

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
