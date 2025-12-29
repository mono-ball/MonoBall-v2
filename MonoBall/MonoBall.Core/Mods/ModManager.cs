using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonoBall.Core.Constants;
using MonoBall.Core.Mods.Utilities;
using Serilog;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Main entry point for the mod system. Manages mod loading and provides access to definitions.
    /// </summary>
    public class ModManager : IModManager, IDisposable
    {
        private readonly DefinitionRegistry _registry;
        private readonly ModLoader _loader;
        private readonly ModValidator _validator;
        private readonly ILogger _logger;
        private bool _isLoaded = false;

        /// <summary>
        /// Initializes a new instance of the ModManager.
        /// </summary>
        /// <param name="modsDirectory">Path to the Mods directory. Defaults to "Mods" relative to the executable.</param>
        /// <param name="logger">The logger instance for logging mod management messages.</param>
        public ModManager(string? modsDirectory = null, ILogger? logger = null)
        {
            modsDirectory ??= ModsPathResolver.FindModsDirectory();
            if (modsDirectory == null)
            {
                modsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
            }
            else if (!Path.IsPathRooted(modsDirectory))
            {
                modsDirectory = ModsPathResolver.ResolveModsDirectory(modsDirectory);
            }

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registry = new DefinitionRegistry();
            _loader = new ModLoader(modsDirectory, _registry, _logger);
            _validator = new ModValidator(modsDirectory, _logger);
        }

        /// <summary>
        /// Gets the definition registry. Only available after Load() has been called.
        /// </summary>
        public DefinitionRegistry Registry
        {
            get
            {
                if (!_isLoaded)
                {
                    throw new InvalidOperationException(
                        "Mods must be loaded before accessing the registry. Call Load() first."
                    );
                }
                return _registry;
            }
        }

        /// <summary>
        /// Gets the list of loaded mods. Only available after Load() has been called.
        /// </summary>
        public IReadOnlyList<ModManifest> LoadedMods
        {
            get
            {
                if (!_isLoaded)
                {
                    throw new InvalidOperationException(
                        "Mods must be loaded before accessing loaded mods. Call Load() first."
                    );
                }
                return _loader.LoadedMods;
            }
        }

        /// <summary>
        /// Validates all mods without loading them.
        /// </summary>
        /// <returns>List of validation issues found.</returns>
        public List<ValidationIssue> Validate()
        {
            return _validator.ValidateAll();
        }

        /// <summary>
        /// Loads all mods and their definitions. This should be called once during game initialization.
        /// </summary>
        /// <param name="validationErrors">Optional list to populate with validation errors/warnings.</param>
        /// <returns>True if loading succeeded, false if there were critical errors.</returns>
        public bool Load(List<string>? validationErrors = null)
        {
            if (_isLoaded)
            {
                throw new InvalidOperationException(
                    "Mods have already been loaded. ModManager instances should only load once."
                );
            }

            _logger.Information("Starting mod loading process");

            // First validate
            var validationIssues = Validate();
            var hasErrors = validationIssues.Any(i => i.Severity == ValidationSeverity.Error);

            if (validationErrors != null)
            {
                foreach (var issue in validationIssues)
                {
                    var severity = issue.Severity switch
                    {
                        ValidationSeverity.Error => "ERROR",
                        ValidationSeverity.Warning => "WARNING",
                        _ => "INFO",
                    };
                    validationErrors.Add(
                        $"[{severity}] {issue.Message}"
                            + (issue.ModId != null ? $" (Mod: {issue.ModId})" : "")
                            + (issue.FilePath != null ? $" (File: {issue.FilePath})" : "")
                    );
                }
            }

            // Load mods (will still attempt to load even with validation errors)
            var loadErrors = _loader.LoadAllMods();
            if (validationErrors != null)
            {
                validationErrors.AddRange(loadErrors);
            }

            _isLoaded = true;

            var success = !hasErrors && loadErrors.Count == 0;
            if (success)
            {
                _logger.Information("Mod loading completed successfully");
            }
            else
            {
                _logger.Warning(
                    "Mod loading completed with {ErrorCount} validation errors and {LoadErrorCount} load errors",
                    validationIssues.Count(i => i.Severity == ValidationSeverity.Error),
                    loadErrors.Count
                );
            }

            // Return false if there were critical errors
            return success;
        }

        /// <summary>
        /// Gets a definition by ID as a strongly-typed object.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the definition to.</typeparam>
        /// <param name="id">The definition ID.</param>
        /// <returns>The deserialized definition, or null if not found.</returns>
        public T? GetDefinition<T>(string id)
            where T : class
        {
            return Registry.GetById<T>(id);
        }

        /// <summary>
        /// Gets a definition metadata by ID.
        /// </summary>
        /// <param name="id">The definition ID.</param>
        /// <returns>The definition metadata, or null if not found.</returns>
        public DefinitionMetadata? GetDefinitionMetadata(string id)
        {
            return Registry.GetById(id);
        }

        /// <summary>
        /// Gets all definitions of a specific type.
        /// </summary>
        /// <param name="definitionType">The definition type (e.g., "FontDefinitions", "TileBehaviorDefinitions").</param>
        /// <returns>List of definition IDs of the specified type.</returns>
        public IEnumerable<string> GetDefinitionsByType(string definitionType)
        {
            return Registry.GetByType(definitionType);
        }

        /// <summary>
        /// Checks if a definition with the given ID exists.
        /// </summary>
        /// <param name="id">The definition ID.</param>
        /// <returns>True if the definition exists, false otherwise.</returns>
        public bool HasDefinition(string id)
        {
            return Registry.Contains(id);
        }

        /// <summary>
        /// Gets the tile width from constants service or mod configuration.
        /// Prioritizes constants service if provided, then falls back to mod configuration.
        /// </summary>
        /// <param name="constantsService">Optional constants service to use. If provided, uses "TileWidth" constant.</param>
        /// <returns>The tile width in pixels.</returns>
        /// <exception cref="InvalidOperationException">Thrown if mods are not loaded or no tile width configuration is available.</exception>
        public int GetTileWidth(IConstantsService? constantsService = null)
        {
            // Prioritize constants service if available
            if (constantsService != null && constantsService.Contains("TileWidth"))
            {
                return constantsService.Get<int>("TileWidth");
            }

            // Fall back to mod configuration (for backward compatibility)
            if (!_isLoaded || LoadedMods.Count == 0)
            {
                throw new InvalidOperationException(
                    "Cannot get tile width: Mods are not loaded and ConstantsService is not available. "
                        + "Ensure mods are loaded and ConstantsService has TileWidth constant defined."
                );
            }

            // Prioritize core mod
            if (CoreMod != null && CoreMod.TileWidth > 0)
            {
                return CoreMod.TileWidth;
            }

            // Fall back to first loaded mod (lowest priority = loaded first)
            var firstMod = LoadedMods.OrderBy(m => m.Priority).First();
            if (firstMod.TileWidth > 0)
            {
                return firstMod.TileWidth;
            }

            throw new InvalidOperationException(
                "Cannot get tile width: No tile width configuration available. "
                    + "Ensure ConstantsService has TileWidth constant defined, or at least one mod has tileWidth specified in mod.json."
            );
        }

        /// <summary>
        /// Gets the tile height from constants service or mod configuration.
        /// Prioritizes constants service if provided, then falls back to mod configuration.
        /// </summary>
        /// <param name="constantsService">Optional constants service to use. If provided, uses "TileHeight" constant.</param>
        /// <returns>The tile height in pixels.</returns>
        /// <exception cref="InvalidOperationException">Thrown if mods are not loaded or no tile height configuration is available.</exception>
        public int GetTileHeight(IConstantsService? constantsService = null)
        {
            // Prioritize constants service if available
            if (constantsService != null && constantsService.Contains("TileHeight"))
            {
                return constantsService.Get<int>("TileHeight");
            }

            // Fall back to mod configuration (for backward compatibility)
            if (!_isLoaded || LoadedMods.Count == 0)
            {
                throw new InvalidOperationException(
                    "Cannot get tile height: Mods are not loaded and ConstantsService is not available. "
                        + "Ensure mods are loaded and ConstantsService has TileHeight constant defined."
                );
            }

            // Prioritize core mod
            if (CoreMod != null && CoreMod.TileHeight > 0)
            {
                return CoreMod.TileHeight;
            }

            // Fall back to first loaded mod (lowest priority = loaded first)
            var firstMod = LoadedMods.OrderBy(m => m.Priority).First();
            if (firstMod.TileHeight > 0)
            {
                return firstMod.TileHeight;
            }

            throw new InvalidOperationException(
                "Cannot get tile height: No tile height configuration available. "
                    + "Ensure ConstantsService has TileHeight constant defined, or at least one mod has tileHeight specified in mod.json."
            );
        }

        /// <summary>
        /// Gets the core mod manifest (slot 0 in mod.manifest).
        /// </summary>
        public ModManifest? CoreMod
        {
            get
            {
                if (!_isLoaded)
                {
                    throw new InvalidOperationException(
                        "Mods must be loaded before accessing core mod. Call Load() first."
                    );
                }
                return _loader.CoreMod;
            }
        }

        /// <summary>
        /// Checks if the specified mod ID is the core mod.
        /// </summary>
        /// <param name="modId">The mod ID to check.</param>
        /// <returns>True if the mod is the core mod, false otherwise.</returns>
        public bool IsCoreMod(string modId)
        {
            if (!_isLoaded)
            {
                throw new InvalidOperationException(
                    "Mods must be loaded before checking if mod is core. Call Load() first."
                );
            }
            return CoreMod?.Id == modId;
        }

        /// <summary>
        /// Gets a mod manifest by ID.
        /// </summary>
        /// <param name="modId">The mod ID.</param>
        /// <returns>The mod manifest, or null if not found.</returns>
        public ModManifest? GetModManifest(string modId)
        {
            if (!_isLoaded)
            {
                throw new InvalidOperationException(
                    "Mods must be loaded before accessing mod manifests. Call Load() first."
                );
            }
            return _loader.GetModManifest(modId);
        }

        /// <summary>
        /// Gets the mod manifest that owns a definition by definition ID.
        /// </summary>
        /// <param name="definitionId">The definition ID.</param>
        /// <returns>The mod manifest that owns the definition, or null if not found.</returns>
        public ModManifest? GetModManifestByDefinitionId(string definitionId)
        {
            if (!_isLoaded)
            {
                throw new InvalidOperationException(
                    "Mods must be loaded before accessing mod manifests. Call Load() first."
                );
            }

            var metadata = GetDefinitionMetadata(definitionId);
            if (metadata == null)
            {
                return null;
            }

            return _loader.GetModManifest(metadata.OriginalModId);
        }

        /// <summary>
        /// Disposes the ModManager and all mod sources.
        /// </summary>
        public void Dispose()
        {
            _loader?.Dispose();
        }
    }
}
