using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Mods.Utilities;
using Serilog;

namespace MonoBall.Core.Mods;

/// <summary>
///     Loads mods from the Mods directory, resolves dependencies, and loads definitions.
/// </summary>
public class ModLoader : IDisposable
{
    private readonly List<ModManifest> _loadedMods = new();
    private readonly ILogger _logger;

    private readonly Dictionary<string, ModManifest> _modsById = new();

    private readonly string _modsDirectory;
    private readonly List<IModSource> _modSources = new();
    private readonly DefinitionRegistry _registry;

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
        modManifests.Remove(coreMod); // Remove from list so it's not loaded again

        // Step 4: Resolve dependencies and determine load order for remaining mods
        var loadOrder = ResolveLoadOrder(modManifests, coreModId, errors);
        _logger.Information("Resolved load order for {ModCount} mods", loadOrder.Count);

        // Step 5: Load remaining mod definitions in order
        foreach (var mod in loadOrder)
        {
            _logger.Debug("Loading definitions for mod: {ModId}", mod.Id);
            LoadModDefinitions(mod, errors);
        }

        // Step 6: Validate BehaviorDefinitions before locking registry
        ValidateBehaviorDefinitions(errors);

        // Step 7: Lock the registry
        _registry.Lock();
        _logger.Information(
            "Mod loading completed. Loaded {ModCount} mods with {ErrorCount} errors",
            _loadedMods.Count,
            errors.Count
        );

        return errors;
    }

    /// <summary>
    ///     Determines the core mod ID from mod.manifest slot 0.
    /// </summary>
    /// <param name="errors">List to populate with errors.</param>
    /// <returns>The core mod ID, or null if not found.</returns>
    private string? DetermineCoreModId(List<string> errors)
    {
        var rootManifestPath = Path.Combine(_modsDirectory, "mod.manifest");
        if (File.Exists(rootManifestPath))
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
                    var coreModId = rootManifest.ModOrder[0];
                    _logger.Debug(
                        "Core mod determined from mod.manifest slot 0: {CoreModId}",
                        coreModId
                    );
                    return coreModId;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error reading root mod.manifest: {ex.Message}");
            }

        errors.Add("mod.manifest not found or empty. Cannot determine core mod (slot 0).");
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

            // Set ModDirectory for backward compatibility
            manifest.ModDirectory = modSource.SourcePath;

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
        var rootManifestPath = Path.Combine(_modsDirectory, "mod.manifest");
        if (File.Exists(rootManifestPath))
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
                            errors.Add(
                                $"Mod '{mod.Id}' not specified in root mod.manifest, added at end"
                            );
                        }

                    // Add to loaded mods list (core mod already added)
                    foreach (var mod in orderedMods)
                        if (!_loadedMods.Contains(mod))
                            _loadedMods.Add(mod);

                    return orderedMods;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error reading root mod.manifest: {ex.Message}");
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
        foreach (var mod in sortedMods)
            if (!_loadedMods.Contains(mod))
                _loadedMods.Add(mod);

        return sortedMods;
    }

    /// <summary>
    ///     Recursively resolves dependencies for a mod.
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

    /// <summary>
    ///     Loads all definitions from a mod.
    /// </summary>
    private void LoadModDefinitions(ModManifest mod, List<string> errors)
    {
        if (mod.ModSource == null)
            throw new InvalidOperationException(
                $"Mod '{mod.Id}' has no ModSource. Mods must have a valid ModSource to load definitions."
            );

        _logger.Debug(
            "Loading definitions for mod {ModId} from {ContentFolderCount} content folders",
            mod.Id,
            mod.ContentFolders.Count
        );

        // Load definitions from each content folder type
        foreach (var (folderType, relativePath) in mod.ContentFolders)
        {
            if (string.IsNullOrEmpty(relativePath))
                continue;

            var normalizedPath = ModPathNormalizer.Normalize(relativePath);
            var jsonFiles = ModPathFilter.FilterByContentFolder(
                mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories),
                normalizedPath
            );

            foreach (var jsonFile in jsonFiles)
                LoadDefinitionFromFile(mod.ModSource, jsonFile, folderType, mod, errors);
        }

        // Load script definitions from Definitions/Scripts/ subdirectories
        var scriptJsonFiles = mod
            .ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories)
            .Where(p =>
                p.StartsWith("Definitions/Scripts/", StringComparison.Ordinal)
                || p.StartsWith("Definitions\\Scripts\\", StringComparison.Ordinal)
            );

        foreach (var jsonFile in scriptJsonFiles)
            LoadDefinitionFromFile(mod.ModSource, jsonFile, "Script", mod, errors);

        // Load behavior definitions from Definitions/Behaviors/ subdirectories
        var behaviorJsonFiles = mod
            .ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories)
            .Where(p =>
                p.StartsWith("Definitions/Behaviors/", StringComparison.Ordinal)
                || p.StartsWith("Definitions\\Behaviors\\", StringComparison.Ordinal)
            );

        foreach (var jsonFile in behaviorJsonFiles)
            LoadDefinitionFromFile(mod.ModSource, jsonFile, "Behavior", mod, errors);
    }

    /// <summary>
    ///     Loads a single definition from a file.
    /// </summary>
    private void LoadDefinitionFromFile(
        IModSource modSource,
        string relativePath,
        string definitionType,
        ModManifest mod,
        List<string> errors
    )
    {
        try
        {
            var jsonContent = modSource.ReadTextFile(relativePath);
            var jsonDoc = JsonDocument.Parse(jsonContent);

            if (!jsonDoc.RootElement.TryGetProperty("id", out var idElement))
            {
                errors.Add($"Definition file '{relativePath}' is missing 'id' field");
                return;
            }

            var id = idElement.GetString();
            if (string.IsNullOrEmpty(id))
            {
                errors.Add($"Definition file '{relativePath}' has empty 'id' field");
                return;
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
                    finalData = JsonElementMerger.Merge(
                        existing.Data,
                        jsonDoc.RootElement,
                        operation == DefinitionOperation.Extend
                    );
                // For Replace, use the new data as-is

                var metadata = new DefinitionMetadata
                {
                    Id = id,
                    OriginalModId = existing.OriginalModId,
                    LastModifiedByModId = mod.Id,
                    Operation = operation,
                    DefinitionType = definitionType,
                    Data = finalData,
                    SourcePath = relativePath, // Already relative, no conversion needed
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
                    SourcePath = relativePath, // Already relative, no conversion needed
                };

                _registry.Register(metadata);
            }
        }
        catch (JsonException ex)
        {
            var errorMessage = $"JSON error in definition file '{relativePath}': {ex.Message}";
            _logger.Error(ex, errorMessage);
            errors.Add(errorMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error loading definition from '{relativePath}': {ex.Message}";
            _logger.Error(ex, errorMessage);
            errors.Add(errorMessage);
        }
    }

    /// <summary>
    ///     Validates all loaded BehaviorDefinitions against their referenced ScriptDefinitions.
    ///     Throws exceptions for invalid definitions (fail fast).
    /// </summary>
    private void ValidateBehaviorDefinitions(List<string> errors)
    {
        // Get all BehaviorDefinition IDs from registry
        var behaviorDefinitionIds = _registry.GetByType("Behavior").ToList();
        if (behaviorDefinitionIds.Count == 0)
            return; // No behavior definitions to validate

        foreach (var behaviorId in behaviorDefinitionIds)
        {
            var behaviorDef = _registry.GetById<BehaviorDefinition>(behaviorId);
            if (behaviorDef == null)
                continue; // Skip if not found (shouldn't happen, but be safe)

            // Validate scriptId is not empty
            if (string.IsNullOrWhiteSpace(behaviorDef.ScriptId))
            {
                var errorMessage =
                    $"BehaviorDefinition '{behaviorId}' has empty scriptId. BehaviorDefinition must reference a valid ScriptDefinition.";
                _logger.Error(errorMessage);
                errors.Add(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            // Look up referenced ScriptDefinition
            var scriptDef = _registry.GetById<ScriptDefinition>(behaviorDef.ScriptId);
            if (scriptDef == null)
            {
                var errorMessage =
                    $"ScriptDefinition '{behaviorDef.ScriptId}' not found for BehaviorDefinition '{behaviorId}'. Ensure the script definition exists and is loaded.";
                _logger.Error(errorMessage);
                errors.Add(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            // Validate parameterOverrides
            if (behaviorDef.ParameterOverrides != null && behaviorDef.ParameterOverrides.Count > 0)
            {
                if (scriptDef.Parameters == null || scriptDef.Parameters.Count == 0)
                {
                    var errorMessage =
                        $"BehaviorDefinition '{behaviorId}' has parameterOverrides but ScriptDefinition '{behaviorDef.ScriptId}' has no parameters defined.";
                    _logger.Error(errorMessage);
                    errors.Add(errorMessage);
                    throw new InvalidOperationException(errorMessage);
                }

                foreach (var kvp in behaviorDef.ParameterOverrides)
                {
                    var paramName = kvp.Key;
                    var paramValue = kvp.Value;

                    // Check if parameter exists in ScriptDefinition
                    var paramDef = scriptDef.Parameters.FirstOrDefault(p => p.Name == paramName);
                    if (paramDef == null)
                    {
                        var errorMessage =
                            $"BehaviorDefinition '{behaviorId}' has parameterOverride '{paramName}' that does not exist in ScriptDefinition '{behaviorDef.ScriptId}'. Valid parameters: {string.Join(", ", scriptDef.Parameters.Select(p => p.Name))}.";
                        _logger.Error(errorMessage);
                        errors.Add(errorMessage);
                        throw new InvalidOperationException(errorMessage);
                    }

                    // Validate parameter type (basic check - full type conversion happens in MapLoaderSystem)
                    // JSON deserialization may return JsonElement for numeric values, which is acceptable
                    // We just verify the JsonElement can be converted to the expected type
                    try
                    {
                        var paramType = paramDef.Type.ToLowerInvariant();

                        // Handle JsonElement from JSON deserialization
                        if (paramValue is JsonElement jsonElement)
                            // Try to get the value type from JsonElement
                            switch (paramType)
                            {
                                case "int":
                                    if (
                                        jsonElement.ValueKind == JsonValueKind.Number
                                        || jsonElement.ValueKind == JsonValueKind.String
                                    )
                                    {
                                        // Can be converted, validation passes
                                    }
                                    else
                                    {
                                        throw new InvalidCastException(
                                            $"JsonElement with ValueKind {jsonElement.ValueKind} cannot be converted to int"
                                        );
                                    }

                                    break;
                                case "float":
                                    if (
                                        jsonElement.ValueKind == JsonValueKind.Number
                                        || jsonElement.ValueKind == JsonValueKind.String
                                    )
                                    {
                                        // Can be converted, validation passes
                                    }
                                    else
                                    {
                                        throw new InvalidCastException(
                                            $"JsonElement with ValueKind {jsonElement.ValueKind} cannot be converted to float"
                                        );
                                    }

                                    break;
                                case "bool":
                                    if (
                                        jsonElement.ValueKind == JsonValueKind.True
                                        || jsonElement.ValueKind == JsonValueKind.False
                                        || jsonElement.ValueKind == JsonValueKind.String
                                    )
                                    {
                                        // Can be converted, validation passes
                                    }
                                    else
                                    {
                                        throw new InvalidCastException(
                                            $"JsonElement with ValueKind {jsonElement.ValueKind} cannot be converted to bool"
                                        );
                                    }

                                    break;
                                case "string":
                                    // String can accept anything (will be converted to string)
                                    break;
                                case "vector2":
                                    if (jsonElement.ValueKind == JsonValueKind.String)
                                    {
                                        // Can be converted, validation passes
                                    }
                                    else
                                    {
                                        throw new InvalidCastException(
                                            $"JsonElement with ValueKind {jsonElement.ValueKind} cannot be converted to Vector2 (must be string)"
                                        );
                                    }

                                    break;
                            }
                        else
                            // Handle already-deserialized types
                            switch (paramType)
                            {
                                case "int":
                                    if (
                                        paramValue is not (int or long or double or float or string)
                                    )
                                        throw new InvalidCastException(
                                            $"Cannot convert {paramValue.GetType()} to int"
                                        );
                                    break;
                                case "float":
                                    if (
                                        paramValue is not (float or double or int or long or string)
                                    )
                                        throw new InvalidCastException(
                                            $"Cannot convert {paramValue.GetType()} to float"
                                        );
                                    break;
                                case "bool":
                                    if (paramValue is not (bool or string))
                                        throw new InvalidCastException(
                                            $"Cannot convert {paramValue.GetType()} to bool"
                                        );
                                    break;
                                case "string":
                                    // String can accept anything (will be converted to string)
                                    break;
                                case "vector2":
                                    if (paramValue is not string)
                                        throw new InvalidCastException(
                                            "Vector2 must be string format 'X,Y'"
                                        );
                                    break;
                            }
                    }
                    catch (Exception ex)
                    {
                        var errorMessage =
                            $"BehaviorDefinition '{behaviorId}' has parameterOverride '{paramName}' with invalid type. Expected '{paramDef.Type}', got '{paramValue.GetType()}'. Error: {ex.Message}";
                        _logger.Error(ex, errorMessage);
                        errors.Add(errorMessage);
                        throw new InvalidOperationException(errorMessage, ex);
                    }

                    // Validate min/max bounds if numeric
                    if (paramDef.Min != null || paramDef.Max != null)
                    {
                        double? numericValue = null;

                        // Handle JsonElement from JSON deserialization
                        if (paramValue is JsonElement jsonElement)
                        {
                            if (jsonElement.ValueKind == JsonValueKind.Number)
                            {
                                if (jsonElement.TryGetDouble(out var doubleValue))
                                    numericValue = doubleValue;
                                else if (jsonElement.TryGetInt64(out var longValue))
                                    numericValue = longValue;
                            }
                            else if (jsonElement.ValueKind == JsonValueKind.String)
                            {
                                if (double.TryParse(jsonElement.GetString(), out var parsed))
                                    numericValue = parsed;
                            }
                        }
                        else
                        {
                            // Handle already-deserialized types
                            numericValue = paramValue switch
                            {
                                int i => i,
                                long l => l,
                                float f => f,
                                double d => d,
                                string s when double.TryParse(s, out var parsed) => parsed,
                                _ => null,
                            };
                        }

                        if (numericValue != null)
                        {
                            if (paramDef.Min != null && numericValue < paramDef.Min)
                            {
                                var errorMessage =
                                    $"BehaviorDefinition '{behaviorId}' has parameterOverride '{paramName}' value '{paramValue}' below minimum '{paramDef.Min}' for ScriptDefinition '{behaviorDef.ScriptId}'. Value must be >= {paramDef.Min}.";
                                _logger.Error(errorMessage);
                                errors.Add(errorMessage);
                                throw new ArgumentException(errorMessage);
                            }

                            if (paramDef.Max != null && numericValue > paramDef.Max)
                            {
                                var errorMessage =
                                    $"BehaviorDefinition '{behaviorId}' has parameterOverride '{paramName}' value '{paramValue}' above maximum '{paramDef.Max}' for ScriptDefinition '{behaviorDef.ScriptId}'. Value must be <= {paramDef.Max}.";
                                _logger.Error(errorMessage);
                                errors.Add(errorMessage);
                                throw new ArgumentException(errorMessage);
                            }
                        }
                    }
                }
            }
        }
    }
}
