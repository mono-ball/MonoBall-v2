using MonoBall.Core.Mods;

namespace MonoBall.Core.Resources
{
    /// <summary>
    /// Service for resolving resource file paths from mod definitions.
    /// Fails fast with exceptions per .cursorrules (no fallback code).
    /// </summary>
    public interface IResourcePathResolver
    {
        /// <summary>
        /// Resolves the full file path for a resource definition.
        /// Fails fast with exceptions per .cursorrules (no fallback code).
        /// </summary>
        /// <param name="resourceId">The resource definition ID.</param>
        /// <param name="relativePath">The relative path from the definition (e.g., TexturePath, FontPath).</param>
        /// <returns>The full absolute path.</returns>
        /// <exception cref="ArgumentException">Thrown when resourceId or relativePath is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when mod manifest cannot be found.</exception>
        /// <exception cref="FileNotFoundException">Thrown when resolved file does not exist.</exception>
        string ResolveResourcePath(string resourceId, string relativePath);

        /// <summary>
        /// Gets the mod manifest that owns a resource definition.
        /// Fails fast with exception if not found (no fallback code).
        /// </summary>
        /// <param name="resourceId">The resource definition ID.</param>
        /// <returns>The mod manifest.</returns>
        /// <exception cref="ArgumentException">Thrown when resourceId is null/empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when mod manifest cannot be found.</exception>
        ModManifest GetResourceModManifest(string resourceId);
    }
}
