using System.Collections.Generic;
using MonoBall.Core.Mods.Utilities;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Interface for mod management functionality.
    /// Provides access to mod loading, validation, and definition registry.
    /// </summary>
    public interface IModManager
    {
        /// <summary>
        /// Gets the definition registry. Only available after Load() has been called.
        /// </summary>
        DefinitionRegistry Registry { get; }

        /// <summary>
        /// Gets the list of loaded mods. Only available after Load() has been called.
        /// </summary>
        IReadOnlyList<ModManifest> LoadedMods { get; }

        /// <summary>
        /// Validates all mods without loading them.
        /// </summary>
        /// <returns>List of validation issues found.</returns>
        List<ValidationIssue> Validate();

        /// <summary>
        /// Loads all mods and their definitions. This should be called once during game initialization.
        /// </summary>
        /// <param name="validationErrors">Optional list to populate with validation errors/warnings.</param>
        /// <returns>True if loading succeeded, false if there were critical errors.</returns>
        bool Load(List<string>? validationErrors = null);

        /// <summary>
        /// Gets a definition by ID and type.
        /// </summary>
        /// <typeparam name="T">The type of definition to retrieve.</typeparam>
        /// <param name="id">The definition ID.</param>
        /// <returns>The definition, or null if not found.</returns>
        T? GetDefinition<T>(string id)
            where T : class;

        /// <summary>
        /// Gets definition metadata by ID.
        /// </summary>
        /// <param name="id">The definition ID.</param>
        /// <returns>The definition metadata, or null if not found.</returns>
        DefinitionMetadata? GetDefinitionMetadata(string id);

        /// <summary>
        /// Gets the tile width from mod configuration.
        /// Prioritizes the core mod (base:monoball-core), then falls back to the first loaded mod.
        /// </summary>
        /// <returns>The tile width in pixels.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown if mods are not loaded or no mods have tile width configuration.</exception>
        int GetTileWidth();

        /// <summary>
        /// Gets the tile height from mod configuration.
        /// Prioritizes the core mod (base:monoball-core), then falls back to the first loaded mod.
        /// </summary>
        /// <returns>The tile height in pixels.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown if mods are not loaded or no mods have tile height configuration.</exception>
        int GetTileHeight();
    }
}
