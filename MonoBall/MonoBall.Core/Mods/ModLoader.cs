using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MonoBall.Core.Mods.Utilities;
using Serilog;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Loads mods from the Mods directory, resolves dependencies, and loads definitions.
    /// </summary>
    public class ModLoader
    {
        private readonly string _modsDirectory;
        private readonly DefinitionRegistry _registry;
        private readonly ILogger _logger;
        private readonly List<ModManifest> _loadedMods = new List<ModManifest>();
        private readonly Dictionary<string, ModManifest> _modsById =
            new Dictionary<string, ModManifest>();

        /// <summary>
        /// Initializes a new instance of the ModLoader.
        /// </summary>
        /// <param name="modsDirectory">Path to the Mods directory.</param>
        /// <param name="registry">The definition registry to populate.</param>
        /// <param name="logger">The logger instance for logging mod loading messages.</param>
        public ModLoader(string modsDirectory, DefinitionRegistry registry, ILogger logger)
        {
            _modsDirectory =
                modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the list of loaded mods in load order.
        /// </summary>
        public IReadOnlyList<ModManifest> LoadedMods => _loadedMods.AsReadOnly();

        /// <summary>
        /// Loads all mods from the Mods directory, resolves load order, and loads all definitions.
        /// </summary>
        /// <returns>List of validation errors and warnings found during loading.</returns>
        public List<string> LoadAllMods()
        {
            _logger.Information(
                "Starting mod loading from directory: {ModsDirectory}",
                _modsDirectory
            );
            var errors = new List<string>();

            // Step 1: Discover and load all mod manifests
            var modManifests = DiscoverMods(errors);
            if (modManifests.Count == 0)
            {
                errors.Add("No mods found in Mods directory.");
                return errors;
            }

            _logger.Information("Discovered {ModCount} mods", modManifests.Count);

            // Step 2: Resolve dependencies and determine load order
            var loadOrder = ResolveLoadOrder(modManifests, errors);
            _logger.Information("Resolved load order for {ModCount} mods", loadOrder.Count);

            // Step 3: Load definitions in order
            foreach (var mod in loadOrder)
            {
                _logger.Debug("Loading definitions for mod: {ModId}", mod.Id);
                LoadModDefinitions(mod, errors);
            }

            // Step 4: Lock the registry
            _registry.Lock();
            _logger.Information(
                "Mod loading completed. Loaded {ModCount} mods with {ErrorCount} errors",
                loadOrder.Count,
                errors.Count
            );

            return errors;
        }

        /// <summary>
        /// Discovers all mods in the Mods directory.
        /// </summary>
        private List<ModManifest> DiscoverMods(List<string> errors)
        {
            var mods = new List<ModManifest>();

            if (!Directory.Exists(_modsDirectory))
            {
                errors.Add($"Mods directory does not exist: {_modsDirectory}");
                return mods;
            }

            var modDirectories = Directory.GetDirectories(_modsDirectory);
            _logger.Debug("Scanning {DirectoryCount} directories for mods", modDirectories.Length);
            foreach (var modDir in modDirectories)
            {
                var modJsonPath = Path.Combine(modDir, "mod.json");
                if (!File.Exists(modJsonPath))
                {
                    errors.Add(
                        $"Mod directory '{Path.GetFileName(modDir)}' does not contain mod.json"
                    );
                    continue;
                }

                try
                {
                    var jsonContent = File.ReadAllText(modJsonPath);
                    var manifest = JsonSerializer.Deserialize<ModManifest>(
                        jsonContent,
                        JsonSerializerOptionsFactory.ForManifests
                    );
                    if (manifest == null)
                    {
                        errors.Add(
                            $"Failed to deserialize mod.json in '{Path.GetFileName(modDir)}'"
                        );
                        continue;
                    }

                    // Validate required fields
                    if (string.IsNullOrEmpty(manifest.Id))
                    {
                        errors.Add(
                            $"Mod in '{Path.GetFileName(modDir)}' has missing or empty 'id' field"
                        );
                        continue;
                    }

                    if (_modsById.ContainsKey(manifest.Id))
                    {
                        errors.Add(
                            $"Duplicate mod ID '{manifest.Id}' found in '{Path.GetFileName(modDir)}'"
                        );
                        continue;
                    }

                    manifest.ModDirectory = modDir;
                    mods.Add(manifest);
                    _modsById[manifest.Id] = manifest;
                    _logger.Debug(
                        "Discovered mod: {ModId} ({ModName})",
                        manifest.Id,
                        manifest.Name
                    );
                }
                catch (JsonException ex)
                {
                    errors.Add(
                        $"JSON error in mod.json for '{Path.GetFileName(modDir)}': {ex.Message}"
                    );
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"Error loading mod from '{Path.GetFileName(modDir)}': {ex.Message}"
                    );
                }
            }

            _logger.Debug("Discovery completed: {ModCount} mods found", mods.Count);
            return mods;
        }

        /// <summary>
        /// Resolves mod load order based on priority and dependencies.
        /// </summary>
        private List<ModManifest> ResolveLoadOrder(List<ModManifest> mods, List<string> errors)
        {
            // First, check for a root-level mod.manifest file
            var rootManifestPath = Path.Combine(_modsDirectory, "mod.manifest");
            if (File.Exists(rootManifestPath))
            {
                try
                {
                    var rootManifestContent = File.ReadAllText(rootManifestPath);
                    var rootManifest = JsonSerializer.Deserialize<RootModManifest>(
                        rootManifestContent,
                        JsonSerializerOptionsFactory.ForManifests
                    );

                    if (
                        rootManifest != null
                        && rootManifest.ModOrder != null
                        && rootManifest.ModOrder.Count > 0
                    )
                    {
                        // Use explicit load order from root manifest
                        var orderedMods = new List<ModManifest>();
                        var modsById = mods.ToDictionary(m => m.Id);

                        foreach (var modId in rootManifest.ModOrder)
                        {
                            if (modsById.TryGetValue(modId, out var mod))
                            {
                                orderedMods.Add(mod);
                            }
                            else
                            {
                                errors.Add(
                                    $"Mod '{modId}' specified in root mod.manifest not found"
                                );
                            }
                        }

                        // Add any mods not in the root manifest at the end
                        foreach (var mod in mods)
                        {
                            if (!orderedMods.Contains(mod))
                            {
                                orderedMods.Add(mod);
                                errors.Add(
                                    $"Mod '{mod.Id}' not specified in root mod.manifest, added at end"
                                );
                            }
                        }

                        _loadedMods.AddRange(orderedMods);
                        return orderedMods;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error reading root mod.manifest: {ex.Message}");
                }
            }

            // Fall back to priority-based ordering with dependency resolution
            var sortedMods = new List<ModManifest>();
            var processed = new HashSet<string>();
            var processing = new HashSet<string>();

            // Sort by priority first
            var modsByPriority = mods.OrderBy(m => m.Priority).ThenBy(m => m.Id).ToList();

            foreach (var mod in modsByPriority)
            {
                if (!processed.Contains(mod.Id))
                {
                    ResolveDependencies(mod, mods, sortedMods, processed, processing, errors);
                }
            }

            _loadedMods.AddRange(sortedMods);
            return sortedMods;
        }

        /// <summary>
        /// Recursively resolves dependencies for a mod.
        /// </summary>
        private void ResolveDependencies(
            ModManifest mod,
            List<ModManifest> allMods,
            List<ModManifest> sortedMods,
            HashSet<string> processed,
            HashSet<string> processing,
            List<string> errors
        )
        {
            if (processed.Contains(mod.Id))
            {
                return;
            }

            if (processing.Contains(mod.Id))
            {
                errors.Add($"Circular dependency detected involving mod '{mod.Id}'");
                return;
            }

            processing.Add(mod.Id);

            // Process dependencies first
            foreach (var depId in mod.Dependencies)
            {
                var dependency = allMods.FirstOrDefault(m => m.Id == depId);
                if (dependency == null)
                {
                    errors.Add($"Mod '{mod.Id}' depends on '{depId}' which is not found");
                }
                else
                {
                    ResolveDependencies(
                        dependency,
                        allMods,
                        sortedMods,
                        processed,
                        processing,
                        errors
                    );
                }
            }

            processing.Remove(mod.Id);
            processed.Add(mod.Id);
            sortedMods.Add(mod);
        }

        /// <summary>
        /// Loads all definitions from a mod.
        /// </summary>
        private void LoadModDefinitions(ModManifest mod, List<string> errors)
        {
            _logger.Debug(
                "Loading definitions for mod {ModId} from {ContentFolderCount} content folders",
                mod.Id,
                mod.ContentFolders.Count
            );
            // Load definitions from each content folder type
            foreach (var (folderType, relativePath) in mod.ContentFolders)
            {
                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                var definitionsPath = Path.Combine(mod.ModDirectory, relativePath);
                if (!Directory.Exists(definitionsPath))
                {
                    continue; // Not an error, mod might not have all content types
                }

                LoadDefinitionsFromDirectory(definitionsPath, folderType, mod, errors);
            }
        }

        /// <summary>
        /// Recursively loads all JSON definition files from a directory.
        /// </summary>
        private void LoadDefinitionsFromDirectory(
            string directory,
            string definitionType,
            ModManifest mod,
            List<string> errors
        )
        {
            try
            {
                var jsonFiles = Directory.GetFiles(
                    directory,
                    "*.json",
                    SearchOption.AllDirectories
                );
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(jsonFile);
                        var jsonDoc = JsonDocument.Parse(jsonContent);

                        if (!jsonDoc.RootElement.TryGetProperty("id", out var idElement))
                        {
                            errors.Add($"Definition file '{jsonFile}' is missing 'id' field");
                            continue;
                        }

                        var id = idElement.GetString();
                        if (string.IsNullOrEmpty(id))
                        {
                            errors.Add($"Definition file '{jsonFile}' has empty 'id' field");
                            continue;
                        }

                        // Determine operation type (defaults to Create, but can be specified)
                        var operation = DefinitionOperation.Create;
                        if (jsonDoc.RootElement.TryGetProperty("$operation", out var opElement))
                        {
                            var opString = opElement.GetString()?.ToLowerInvariant();
                            operation = opString switch
                            {
                                "modify" => DefinitionOperation.Modify,
                                "extend" => DefinitionOperation.Extend,
                                "replace" => DefinitionOperation.Replace,
                                _ => DefinitionOperation.Create,
                            };
                        }

                        // Check if definition already exists
                        var existing = _registry.GetById(id);
                        if (existing != null)
                        {
                            // Apply operation
                            var finalData = jsonDoc.RootElement;
                            if (
                                operation == DefinitionOperation.Modify
                                || operation == DefinitionOperation.Extend
                            )
                            {
                                finalData = JsonElementMerger.Merge(
                                    existing.Data,
                                    jsonDoc.RootElement,
                                    operation == DefinitionOperation.Extend
                                );
                            }
                            // For Replace, use the new data as-is

                            var metadata = new DefinitionMetadata
                            {
                                Id = id,
                                OriginalModId = existing.OriginalModId,
                                LastModifiedByModId = mod.Id,
                                Operation = operation,
                                DefinitionType = definitionType,
                                Data = finalData,
                                SourcePath = Path.GetRelativePath(mod.ModDirectory, jsonFile),
                            };

                            _registry.Register(metadata);
                        }
                        else
                        {
                            // New definition
                            var metadata = new DefinitionMetadata
                            {
                                Id = id,
                                OriginalModId = mod.Id,
                                LastModifiedByModId = mod.Id,
                                Operation = DefinitionOperation.Create,
                                DefinitionType = definitionType,
                                Data = jsonDoc.RootElement,
                                SourcePath = Path.GetRelativePath(mod.ModDirectory, jsonFile),
                            };

                            _registry.Register(metadata);
                        }
                    }
                    catch (JsonException ex)
                    {
                        var errorMessage =
                            $"JSON error in definition file '{jsonFile}': {ex.Message}";
                        _logger.Error(ex, errorMessage);
                        errors.Add(errorMessage);
                    }
                    catch (Exception ex)
                    {
                        var errorMessage =
                            $"Error loading definition from '{jsonFile}': {ex.Message}";
                        _logger.Error(ex, errorMessage);
                        errors.Add(errorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error scanning directory '{directory}': {ex.Message}");
            }
        }
    }
}
