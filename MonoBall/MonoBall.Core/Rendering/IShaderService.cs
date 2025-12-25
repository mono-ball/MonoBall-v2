using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for loading and managing shader effects.
    /// </summary>
    public interface IShaderService
    {
        /// <summary>
        /// Loads a shader effect from content.
        /// </summary>
        /// <param name="shaderId">The shader ID (e.g., "TileLayerColorGrading").</param>
        /// <returns>The loaded Effect.</returns>
        /// <exception cref="InvalidOperationException">Thrown when shader fails to load.</exception>
        Effect LoadShader(string shaderId);

        /// <summary>
        /// Gets a cached shader effect, or loads it if not cached.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>The Effect.</returns>
        /// <exception cref="InvalidOperationException">Thrown when shader fails to load.</exception>
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
