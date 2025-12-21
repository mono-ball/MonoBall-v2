using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonoBall.Core.Mods.Utilities;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Main entry point for the mod system. Manages mod loading and provides access to definitions.
    /// </summary>
    public class ModManager : IModManager
    {
        private readonly DefinitionRegistry _registry;
        private readonly ModLoader _loader;
        private readonly ModValidator _validator;
        private bool _isLoaded = false;

        /// <summary>
        /// Initializes a new instance of the ModManager.
        /// </summary>
        /// <param name="modsDirectory">Path to the Mods directory. Defaults to "Mods" relative to the executable.</param>
        public ModManager(string? modsDirectory = null)
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

            _registry = new DefinitionRegistry();
            _loader = new ModLoader(modsDirectory, _registry);
            _validator = new ModValidator(modsDirectory);
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

            // Return false if there were critical errors
            return !hasErrors && loadErrors.Count == 0;
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
    }
}
