using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Mods.TypeInference;
using MonoBall.Core.Mods.Utilities;
using Serilog;

namespace MonoBall.Core.Mods;

/// <summary>
///     Loads mods from the Mods directory, resolves dependencies, and loads definitions.
/// </summary>
public class ModLoader : IDisposable
{
    /// <summary>
    ///     List of loaded mods in load order.
    ///     Note: Mod loading is single-threaded. This list is not thread-safe.
    /// </summary>
    private readonly List<ModManifest> _loadedMods = new();
    private readonly ILogger _logger;

    private readonly Dictionary<string, ModManifest> _modsById = new();

    private readonly string _modsDirectory;
    private readonly List<IModSource> _modSources = new();
    private readonly DefinitionRegistry _registry;
    private RootModManifest? _cachedRootManifest;

    /// <summary>
    ///     Initializes a new instance of the ModLoader.
    /// </summary>
    /// <param name="modsDirectory">Path to the Mods directory.</param>
    /// <param name="registry">The definition registry to populate.</param>
    /// <param name="logger">The logger instance for logging mod loading messages.</param>
    public ModLoader(string modsDirectory, DefinitionRegistry registry, ILogger logger)
    {
        _modsDirectory = modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the list of loaded mods in load order.
    /// </summary>
    public IReadOnlyList<ModManifest> LoadedMods => _loadedMods.AsReadOnly();

    /// <summary>
    ///     Gets the core mod manifest (slot 0 in mod.manifest).
    /// </summary>
    public ModManifest? CoreMod { get; private set; }

    /// <summary>
    ///     Disposes all mod sources.
    /// </summary>
    public void Dispose()
    {
        foreach (var modSource in _modSources)
            try
            {
                modSource?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "Error disposing mod source: {SourcePath}",
                    modSource?.SourcePath
                );
            }

        _modSources.Clear();
    }

    /// <summary>
    ///     Gets a mod manifest by ID.
    /// </summary>
    /// <param name="modId">The mod ID.</param>
    /// <returns>The mod manifest, or null if not found.</returns>
    public ModManifest? GetModManifest(string modId)
    {
        if (string.IsNullOrEmpty(modId))
            return null;
        _modsById.TryGetValue(modId, out var manifest);
        return manifest;
    }

    /// <summary>
    ///     Loads all mods from the Mods directory, resolves load order, and loads all definitions.
    ///     Ensures the core mod (slot 0 in mod.manifest) loads first for system-critical resources.
    /// </summary>
    /// <returns>List of validation errors and warnings found during loading.</returns>
    public List<string> LoadAllMods()
    {
        _logger.Information("Starting mod loading from directory: {ModsDirectory}", _modsDirectory);
        var errors = new List<string>();

        // Step 1: Discover and load all mod manifests
        var modManifests = DiscoverMods(errors);
        if (modManifests.Count == 0)
        {
            errors.Add("No mods found in Mods directory.");
            return errors;
        }

        _logger.Information("Discovered {ModCount} mods", modManifests.Count);

        // Step 2: Determine core mod from mod.manifest slot 0
        var coreModId = DetermineCoreModId(errors);
        if (string.IsNullOrEmpty(coreModId))
        {
            errors.Add(
                "Core mod (slot 0 in mod.manifest) not found. System-critical resources may not be available."
            );
            return errors;
        }

        // Step 3: Load core mod FIRST for system-critical resources
        var coreMod = modManifests.FirstOrDefault(m => m.Id == coreModId);
        if (coreMod == null)
        {
            errors.Add(
                $"Core mod '{coreModId}' (slot 0 in mod.manifest) not found. System-critical resources may not be available."
            );
            return errors;
        }

        _logger.Information(
            "Loading core mod '{CoreModId}' (slot 0) first for system-critical resources",
            coreModId
        );
        LoadModDefinitions(coreMod, errors);
        _loadedMods.Add(coreMod);
        CoreMod = coreMod;

        // Step 4: Resolve dependencies and determine load order for remaining mods
        // Create new list excluding core mod to avoid modifying input parameter
        var remainingMods = modManifests.Where(m => m.Id != coreModId).ToList();
        var loadOrder = ResolveLoadOrder(remainingMods, coreModId, errors);
        _logger.Information("Resolved load order for {ModCount} mods", loadOrder.Count);

        // Step 5: Load remaining mod definitions in order
        foreach (var mod in loadOrder)
        {
            _logger.Debug("Loading definitions for mod: {ModId}", mod.Id);
            LoadModDefinitions(mod, errors);
        }

        // Step 6: Lock the registry
        _registry.Lock();
        _logger.Information(
            "Mod loading completed. Loaded {ModCount} mods with {ErrorCount} errors",
            _loadedMods.Count,
            errors.Count
        );

        return errors;
    }

    /// <summary>
    ///     Loads and caches the root mod.manifest file.
    /// </summary>
    /// <param name="errors">List to populate with errors.</param>
    /// <returns>The root manifest, or null if not found or invalid.</returns>
    private RootModManifest? LoadRootManifest(List<string> errors)
    {
        if (_cachedRootManifest != null)
            return _cachedRootManifest;

        var rootManifestPath = Path.Combine(_modsDirectory, "mod.manifest");
        if (!File.Exists(rootManifestPath))
        {
            errors.Add("mod.manifest not found or empty. Cannot determine core mod (slot 0).");
            return null;
        }

        try
        {
            var rootManifestContent = File.ReadAllText(rootManifestPath);
            _cachedRootManifest = JsonSerializer.Deserialize<RootModManifest>(
                rootManifestContent,
                JsonSerializerOptionsFactory.ForManifests
            );
            return _cachedRootManifest;
        }
        catch (Exception ex)
        {
            errors.Add($"Error reading root mod.manifest: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Determines the core mod ID from mod.manifest slot 0.
    /// </summary>
    /// <param name="errors">List to populate with errors.</param>
    /// <returns>The core mod ID, or null if not found.</returns>
    private string? DetermineCoreModId(List<string> errors)
    {
        var rootManifest = LoadRootManifest(errors);
        if (
            rootManifest != null
            && rootManifest.ModOrder != null
            && rootManifest.ModOrder.Count > 0
        )
        {
            var coreModId = rootManifest.ModOrder[0];
            _logger.Debug("Core mod determined from mod.manifest slot 0: {CoreModId}", coreModId);
            return coreModId;
        }

        return null;
    }

    /// <summary>
    ///     Discovers all mods in the Mods directory.
    /// </summary>
    private List<ModManifest> DiscoverMods(List<string> errors)
    {
        var mods = new List<ModManifest>();

        if (!Directory.Exists(_modsDirectory))
        {
            errors.Add($"Mods directory does not exist: {_modsDirectory}");
            return mods;
        }

        try
        {
            var modSources = ModDiscovery.DiscoverModSources(_modsDirectory);
            _logger.Debug("Scanning mod sources for mods");

            foreach (var modSource in modSources)
            {
                // Force TOC load for archives during discovery for early validation
                if (modSource.IsCompressed && modSource is ArchiveModSource archiveSource)
                    try
                    {
                        archiveSource.GetTOC(); // This validates archive integrity
                    }
                    catch (Exception ex)
                    {
                        errors.Add(
                            $"Archive validation failed for '{modSource.SourcePath}': {ex.Message}"
                        );
                        continue;
                    }

                if (TryLoadModSource(modSource, errors, out var manifest))
                {
                    mods.Add(manifest);
                    _modsById[manifest.Id] = manifest;
                    _modSources.Add(modSource); // Track for disposal
                    _logger.Debug(
                        "Discovered mod: {ModId} ({ModName}) from {SourceType}",
                        manifest.Id,
                        manifest.Name,
                        modSource.IsCompressed ? "archive" : "directory"
                    );
                }
                else
                {
                    // Dispose failed mod source
                    modSource.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error during mod discovery: {ex.Message}");
        }

        _logger.Debug("Discovery completed: {ModCount} mods found", mods.Count);
        return mods;
    }

    /// <summary>
    ///     Attempts to load a mod from a mod source.
    /// </summary>
    /// <param name="modSource">The mod source to load.</param>
    /// <param name="errors">List to populate with errors.</param>
    /// <param name="manifest">The loaded manifest, if successful.</param>
    /// <returns>True if the mod was loaded successfully.</returns>
    private bool TryLoadModSource(
        IModSource modSource,
        List<string> errors,
        out ModManifest manifest
    )
    {
        manifest = null!;

        try
        {
            if (!modSource.FileExists("mod.json"))
            {
                errors.Add($"Mod source '{modSource.SourcePath}' does not contain mod.json");
                return false;
            }

            manifest = modSource.GetManifest();

            // Validate required fields
            if (string.IsNullOrEmpty(manifest.Id))
            {
                errors.Add($"Mod in '{modSource.SourcePath}' has missing or empty 'id' field");
                return false;
            }

            if (_modsById.ContainsKey(manifest.Id))
            {
                errors.Add($"Duplicate mod ID '{manifest.Id}' found in '{modSource.SourcePath}'");
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            errors.Add($"JSON error in mod.json for '{modSource.SourcePath}': {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            errors.Add($"Error loading mod from '{modSource.SourcePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Resolves mod load order based on priority and dependencies.
    /// </summary>
    /// <param name="mods">The mods to order (excluding core mod).</param>
    /// <param name="coreModId">The core mod ID to exclude from ordering.</param>
    /// <param name="errors">List to populate with errors.</param>
    /// <returns>Ordered list of mods.</returns>
    private List<ModManifest> ResolveLoadOrder(
        List<ModManifest> mods,
        string coreModId,
        List<string> errors
    )
    {
        // First, check for a root-level mod.manifest file
        var rootManifest = LoadRootManifest(errors);
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
                // Skip core mod - it was already loaded first
                if (modId == coreModId)
                    continue;

                if (modsById.TryGetValue(modId, out var mod))
                    orderedMods.Add(mod);
                else
                    errors.Add($"Mod '{modId}' specified in root mod.manifest not found");
            }

            // Add any mods not in the root manifest at the end
            // (excluding core mod which was already loaded)
            foreach (var mod in mods)
                if (!orderedMods.Contains(mod) && mod.Id != coreModId)
                {
                    orderedMods.Add(mod);
                    errors.Add($"Mod '{mod.Id}' not specified in root mod.manifest, added at end");
                }

            // Add to loaded mods list (core mod already added)
            AddToLoadedMods(orderedMods);

            return orderedMods;
        }

        // Fall back to priority-based ordering with dependency resolution
        var sortedMods = new List<ModManifest>();
        var processed = new HashSet<string>();
        var processing = new HashSet<string>();

        // Sort by priority first (excluding core mod which was already loaded)
        var modsByPriority = mods.Where(m => m.Id != coreModId)
            .OrderBy(m => m.Priority)
            .ThenBy(m => m.Id)
            .ToList();

        foreach (var mod in modsByPriority)
            if (!processed.Contains(mod.Id))
                ResolveDependencies(mod, mods, sortedMods, processed, processing, errors);

        // Add to loaded mods list (core mod already added)
        AddToLoadedMods(sortedMods);

        return sortedMods;
    }

    /// <summary>
    ///     Adds mods to the loaded mods list, avoiding duplicates.
    /// </summary>
    /// <param name="mods">The mods to add.</param>
    private void AddToLoadedMods(IEnumerable<ModManifest> mods)
    {
        foreach (var mod in mods)
        {
            if (!_loadedMods.Contains(mod))
                _loadedMods.Add(mod);
        }
    }

    /// <summary>
    ///     Recursively resolves dependencies for a mod and adds it to the sorted list in dependency order.
    ///     Detects circular dependencies and adds errors for missing dependencies.
    /// </summary>
    /// <param name="mod">The mod to resolve dependencies for.</param>
    /// <param name="allMods">All available mod manifests.</param>
    /// <param name="sortedMods">The sorted list to add mods to (in dependency order).</param>
    /// <param name="processed">Set of mod IDs that have been fully processed.</param>
    /// <param name="processing">Set of mod IDs currently being processed (for cycle detection).</param>
    /// <param name="errors">List to add errors to.</param>
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
            return;

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
                errors.Add($"Mod '{mod.Id}' depends on '{depId}' which is not found");
            else
                ResolveDependencies(dependency, allMods, sortedMods, processed, processing, errors);
        }

        processing.Remove(mod.Id);
        processed.Add(mod.Id);
        sortedMods.Add(mod);
    }

    // Static readonly strategy array (singleton instances, stateless strategies)
    private static readonly ITypeInferenceStrategy[] DefaultStrategies =
        new ITypeInferenceStrategy[]
        {
            HardcodedPathInferenceStrategy.Instance, // Tier 1: Fastest (no I/O)
            DirectoryNameInferenceStrategy.Instance, // Tier 2: Fast (no I/O)
            JsonTypeOverrideStrategy.Instance, // Tier 3: Slow (JSON parsing)
            ModManifestInferenceStrategy.Instance, // Tier 4: Validation
        };

    /// <summary>
    ///     Loads all definitions from a mod using convention-based discovery.
    /// </summary>
    private void LoadModDefinitions(ModManifest mod, List<string> errors)
    {
        if (mod.ModSource == null)
            throw new InvalidOperationException(
                $"Mod '{mod.Id}' has no ModSource. Mods must have a valid ModSource to load definitions."
            );

        _logger.Debug(
            "Loading definitions for mod {ModId} using convention-based discovery",
            mod.Id
        );

        // Enumerate all JSON files in the mod
        var jsonFiles = mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories);

        foreach (var jsonFile in jsonFiles)
        {
            // Skip mod.json itself
            if (jsonFile.Equals("mod.json", StringComparison.OrdinalIgnoreCase))
                continue;

            ProcessDefinitionFile(jsonFile, mod, errors);
        }
    }

    /// <summary>
    ///     Processes a single definition file: infers type, loads definition, and fires event.
    ///     Collects errors for this file but continues processing other files (error recovery).
    /// </summary>
    /// <param name="jsonFile">The JSON file path to process.</param>
    /// <param name="mod">The mod manifest.</param>
    /// <param name="errors">List to collect errors into.</param>
    private void ProcessDefinitionFile(string jsonFile, ModManifest mod, List<string> errors)
    {
        string definitionType;
        JsonDocument? jsonDoc = null;

        try
        {
            // Attempt inference without parsing JSON (fast path)
            definitionType = InferDefinitionType(jsonFile, null, mod);
        }
        catch (InvalidOperationException)
        {
            // Path-based inference failed - parse JSON for $type field (lazy parsing)
            try
            {
                var jsonContent = mod.ModSource!.ReadTextFile(jsonFile);
                jsonDoc = JsonDocument.Parse(jsonContent);

                // Try inference again with JSON document
                definitionType = InferDefinitionType(jsonFile, jsonDoc, mod);
            }
            catch (Exception parseEx)
            {
                var errorMsg = $"Failed to parse JSON file '{jsonFile}': {parseEx.Message}";
                errors.Add(errorMsg);
                _logger.Error(parseEx, "Failed to parse JSON file {FilePath}", jsonFile);
                return; // Skip this file, continue with others (error recovery)
            }
        }

        // Load definition with inferred type (reuse jsonDoc if available)
        DefinitionLoadResult loadResult;
        try
        {
            loadResult = LoadDefinitionFromFile(
                mod.ModSource!,
                jsonFile,
                definitionType,
                mod,
                jsonDoc
            );
        }
        finally
        {
            // Dispose JsonDocument if we created it
            // LoadDefinitionFromFile clones JsonElements, so it doesn't need the document after return
            jsonDoc?.Dispose();
        }

        if (loadResult.IsError)
        {
            errors.Add(loadResult.Error!);
            _logger.Warning(
                "Failed to load definition from mod {ModId}, file {FilePath}: {Error}",
                mod.Id,
                jsonFile,
                loadResult.Error
            );
            return; // Skip this file, continue with others (error recovery)
        }

        // Fire event for systems to react (ECS/Event integration)
        FireDefinitionDiscoveredEvent(mod, definitionType, jsonFile, loadResult.Metadata!);
    }

    /// <summary>
    ///     Fires the DefinitionDiscoveredEvent for a loaded definition.
    /// </summary>
    private void FireDefinitionDiscoveredEvent(
        ModManifest mod,
        string definitionType,
        string filePath,
        DefinitionMetadata metadata
    )
    {
        var discoveredEvent = new DefinitionDiscoveredEvent
        {
            ModId = mod.Id,
            DefinitionType = definitionType,
            DefinitionId = metadata.Id,
            FilePath = filePath,
            SourceModId = metadata.OriginalModId,
            Operation = metadata.Operation,
        };
        EventBus.Send(ref discoveredEvent);
    }

    /// <summary>
    ///     Main type inference method using Chain of Responsibility pattern.
    ///     Throws InvalidOperationException if type cannot be inferred (fail-fast).
    /// </summary>
    /// <param name="filePath">The file path to infer the type for.</param>
    /// <param name="jsonDoc">Optional JSON document (for lazy parsing when path-based inference fails).</param>
    /// <param name="mod">The mod manifest.</param>
    /// <returns>The inferred definition type.</returns>
    /// <exception cref="InvalidOperationException">Thrown if type cannot be inferred from path or JSON.</exception>
    private string InferDefinitionType(string filePath, JsonDocument? jsonDoc, ModManifest mod)
    {
        var normalizedPath = ModPathNormalizer.Normalize(filePath);

        var context = new TypeInferenceContext
        {
            FilePath = filePath,
            NormalizedPath = normalizedPath,
            JsonDocument = jsonDoc,
            Mod = mod,
            Logger = _logger,
        };

        // Chain of Responsibility: Try each strategy in order
        foreach (var strategy in DefaultStrategies)
        {
            var inferredType = strategy.InferType(context);
            if (inferredType != null)
            {
                // Validate inferred type if mod.json declares custom types
                ValidateInferredType(inferredType, context);
                return inferredType;
            }
        }

        // All strategies failed - throw exception (fail fast, per project rules)
        throw new InvalidOperationException(
            $"Could not infer definition type for '{filePath}'. "
                + "Ensure file follows convention-based directory structure or specify $type field in JSON."
        );
    }

    /// <summary>
    ///     Validates inferred type against mod.json customDefinitionTypes if present.
    ///     Logs a warning if the inferred type is not declared in customDefinitionTypes.
    /// </summary>
    /// <param name="inferredType">The inferred definition type.</param>
    /// <param name="context">The type inference context.</param>
    private void ValidateInferredType(string inferredType, TypeInferenceContext context)
    {
        if (
            context.Mod.CustomDefinitionTypes != null
            && !context.Mod.CustomDefinitionTypes.ContainsValue(inferredType)
        )
        {
            context.Logger.Warning(
                "Inferred type '{Type}' not declared in mod.json customDefinitionTypes for {Path}. "
                    + "Consider adding it to customDefinitionTypes for documentation.",
                inferredType,
                context.FilePath
            );
        }
    }

    /// <summary>
    ///     Loads a single definition from a file with unified error handling.
    ///     Preserves $operation support (Create/Modify/Extend/Replace).
    /// </summary>
    private DefinitionLoadResult LoadDefinitionFromFile(
        IModSource modSource,
        string relativePath,
        string definitionType,
        ModManifest mod,
        JsonDocument? existingJsonDoc = null // Reuse parsed JSON if available
    )
    {
        JsonDocument? jsonDoc = null;
        try
        {
            // Reuse existing JsonDocument if provided, otherwise parse
            if (existingJsonDoc != null)
            {
                jsonDoc = existingJsonDoc;
            }
            else
            {
                var jsonContent = modSource.ReadTextFile(relativePath);
                jsonDoc = JsonDocument.Parse(jsonContent);
            }

            // Extract definition ID from JSON
            if (!jsonDoc.RootElement.TryGetProperty("id", out var idElement))
            {
                return DefinitionLoadResult.Failure(
                    $"Definition file '{relativePath}' in mod '{mod.Id}' is missing required 'id' field."
                );
            }

            var id = idElement.GetString();
            if (string.IsNullOrEmpty(id))
            {
                return DefinitionLoadResult.Failure(
                    $"Definition file '{relativePath}' in mod '{mod.Id}' has empty 'id' field."
                );
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
            DefinitionMetadata metadata;

            if (existing != null)
            {
                // Apply operation (Modify/Extend/Replace)
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

                metadata = new DefinitionMetadata
                {
                    Id = id,
                    OriginalModId = existing.OriginalModId,
                    LastModifiedByModId = mod.Id,
                    Operation = operation,
                    DefinitionType = definitionType,
                    Data = finalData, // Use Data (JsonElement), not RawJson
                    SourcePath = relativePath,
                };
            }
            else
            {
                // New definition - clone JsonElement to avoid disposal issues
                metadata = new DefinitionMetadata
                {
                    Id = id,
                    OriginalModId = mod.Id,
                    LastModifiedByModId = mod.Id,
                    Operation = DefinitionOperation.Create,
                    DefinitionType = definitionType,
                    Data = jsonDoc.RootElement.Clone(), // Clone to avoid disposal issues
                    SourcePath = relativePath,
                };
            }

            // Register in registry
            _registry.Register(metadata);

            // Only dispose if we created the JsonDocument (not if it was passed in)
            if (existingJsonDoc == null)
            {
                jsonDoc.Dispose();
            }

            return DefinitionLoadResult.Success(metadata);
        }
        catch (JsonException ex)
        {
            return DefinitionLoadResult.Failure(
                $"JSON error in definition file '{relativePath}' in mod '{mod.Id}': {ex.Message}"
            );
        }
        catch (Exception ex)
        {
            return DefinitionLoadResult.Failure(
                $"Error loading definition from '{relativePath}' in mod '{mod.Id}': {ex.Message}"
            );
        }
        finally
        {
            // Ensure disposal if we created the document and an exception occurred
            if (existingJsonDoc == null && jsonDoc != null)
            {
                jsonDoc.Dispose();
            }
        }
    }
}
