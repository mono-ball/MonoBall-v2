using System.Collections.Generic;
using MonoBall.Core.Constants;

namespace MonoBall.Core.Mods;

/// <summary>
///     Interface for mod management functionality.
///     Provides access to mod loading, validation, and definition registry.
/// </summary>
public interface IModManager
{
    /// <summary>
    ///     Gets the definition registry. Only available after Load() has been called.
    /// </summary>
    DefinitionRegistry Registry { get; }

    /// <summary>
    ///     Gets the list of loaded mods. Only available after Load() has been called.
    /// </summary>
    IReadOnlyList<ModManifest> LoadedMods { get; }

    /// <summary>
    ///     Gets the core mod manifest (slot 0 in mod.manifest, or first loaded mod).
    /// </summary>
    ModManifest? CoreMod { get; }

    /// <summary>
    ///     Validates all mods without loading them.
    /// </summary>
    /// <returns>List of validation issues found.</returns>
    List<ValidationIssue> Validate();

    /// <summary>
    ///     Loads all mods and their definitions. This should be called once during game initialization.
    /// </summary>
    /// <param name="validationErrors">Optional list to populate with validation errors/warnings.</param>
    /// <returns>True if loading succeeded, false if there were critical errors.</returns>
    bool Load(List<string>? validationErrors = null);

    /// <summary>
    ///     Gets a definition by ID and type.
    /// </summary>
    /// <typeparam name="T">The type of definition to retrieve.</typeparam>
    /// <param name="id">The definition ID.</param>
    /// <returns>The definition, or null if not found.</returns>
    T? GetDefinition<T>(string id)
        where T : class;

    /// <summary>
    ///     Gets definition metadata by ID.
    /// </summary>
    /// <param name="id">The definition ID.</param>
    /// <returns>The definition metadata, or null if not found.</returns>
    DefinitionMetadata? GetDefinitionMetadata(string id);

    /// <summary>
    ///     Gets the tile width from constants service.
    /// </summary>
    /// <param name="constantsService">The constants service to use. Must contain "TileWidth" constant.</param>
    /// <returns>The tile width in pixels.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown if constantsService is null.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown if ConstantsService does not contain TileWidth constant.</exception>
    int GetTileWidth(IConstantsService constantsService);

    /// <summary>
    ///     Gets the tile height from constants service.
    /// </summary>
    /// <param name="constantsService">The constants service to use. Must contain "TileHeight" constant.</param>
    /// <returns>The tile height in pixels.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown if constantsService is null.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown if ConstantsService does not contain TileHeight constant.</exception>
    int GetTileHeight(IConstantsService constantsService);

    /// <summary>
    ///     Checks if the specified mod ID is the core mod.
    /// </summary>
    /// <param name="modId">The mod ID to check.</param>
    /// <returns>True if the mod is the core mod, false otherwise.</returns>
    bool IsCoreMod(string modId);

    /// <summary>
    ///     Gets a mod manifest by ID.
    /// </summary>
    /// <param name="modId">The mod ID.</param>
    /// <returns>The mod manifest, or null if not found.</returns>
    ModManifest? GetModManifest(string modId);

    /// <summary>
    ///     Gets the mod manifest that owns a definition by definition ID.
    /// </summary>
    /// <param name="definitionId">The definition ID.</param>
    /// <returns>The mod manifest that owns the definition, or null if not found.</returns>
    ModManifest? GetModManifestByDefinitionId(string definitionId);
}
