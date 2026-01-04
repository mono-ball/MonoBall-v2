using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MonoBall.Core.Mods.Utilities;
using Serilog;

namespace MonoBall.Core.Mods;

/// <summary>
///     Validates mod definitions and manifests for inconsistencies.
/// </summary>
public class ModValidator
{
    private readonly ILogger _logger;
    private readonly string _modsDirectory;

    /// <summary>
    ///     Initializes a new instance of the ModValidator.
    /// </summary>
    /// <param name="modsDirectory">Path to the Mods directory.</param>
    /// <param name="logger">The logger instance for logging validation messages.</param>
    public ModValidator(string modsDirectory, ILogger logger)
    {
        _modsDirectory = modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Validates all mods and returns a list of inconsistencies found.
    /// </summary>
    /// <returns>List of validation messages (errors and warnings).</returns>
    public List<ValidationIssue> ValidateAll()
    {
        _logger.Debug("Starting mod validation in directory: {ModsDirectory}", _modsDirectory);
        var issues = new List<ValidationIssue>();

        if (!Directory.Exists(_modsDirectory))
        {
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Mods directory does not exist: {_modsDirectory}",
                    ModId = string.Empty,
                    FilePath = string.Empty,
                }
            );
            return issues;
        }

        var modManifests = new Dictionary<string, ModManifest>();
        var definitionIds = new Dictionary<string, List<DefinitionLocation>>();

        // First pass: Load all manifests and collect definition IDs
        try
        {
            var modSources = ModDiscovery.DiscoverModSources(_modsDirectory);
            _logger.Debug("Found mod sources for validation");

            foreach (var modSource in modSources)
                try
                {
                    if (!modSource.FileExists("mod.json"))
                    {
                        issues.Add(
                            new ValidationIssue
                            {
                                Severity = ValidationSeverity.Warning,
                                Message =
                                    $"Mod source '{modSource.SourcePath}' does not contain mod.json",
                                ModId = string.Empty,
                                FilePath = modSource.SourcePath,
                            }
                        );
                        continue;
                    }

                    var manifest = modSource.GetManifest();

                    // Validate manifest fields
                    ValidateManifest(manifest, modSource.SourcePath, issues);

                    if (!string.IsNullOrEmpty(manifest.Id))
                    {
                        if (modManifests.ContainsKey(manifest.Id))
                            issues.Add(
                                new ValidationIssue
                                {
                                    Severity = ValidationSeverity.Error,
                                    Message = $"Duplicate mod ID '{manifest.Id}'",
                                    ModId = manifest.Id,
                                    FilePath = modSource.SourcePath,
                                }
                            );
                        else
                            modManifests[manifest.Id] = manifest;
                    }

                    // Collect definition IDs
                    CollectDefinitionIds(manifest, modSource, definitionIds, issues);
                }
                catch (Exception ex)
                {
                    issues.Add(
                        new ValidationIssue
                        {
                            Severity = ValidationSeverity.Error,
                            Message =
                                $"Error reading mod.json from '{modSource.SourcePath}': {ex.Message}",
                            ModId = string.Empty,
                            FilePath = modSource.SourcePath,
                        }
                    );
                }
        }
        catch (Exception ex)
        {
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Error during mod discovery: {ex.Message}",
                    ModId = string.Empty,
                    FilePath = _modsDirectory,
                }
            );
        }

        // Check for duplicate definition IDs
        foreach (var (id, locations) in definitionIds)
            if (locations.Count > 1)
            {
                var modIds = string.Join(", ", locations.Select(l => l.ModId));
                issues.Add(
                    new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Definition ID '{id}' is defined in multiple mods: {modIds}",
                        ModId = string.Empty,
                        FilePath = string.Join("; ", locations.Select(l => l.FilePath)),
                    }
                );
            }

        // Check for missing dependencies
        foreach (var (modId, manifest) in modManifests)
        foreach (var depId in manifest.Dependencies)
            if (!modManifests.ContainsKey(depId))
                issues.Add(
                    new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Mod '{modId}' depends on '{depId}' which is not found",
                        ModId = modId,
                        FilePath = Path.Combine(_modsDirectory, modId, "mod.json"),
                    }
                );

        // Check for circular dependencies
        foreach (var (modId, manifest) in modManifests)
            CheckCircularDependencies(modId, manifest, modManifests, new HashSet<string>(), issues);

        var errorCount = issues.Count(i => i.Severity == ValidationSeverity.Error);
        var warningCount = issues.Count(i => i.Severity == ValidationSeverity.Warning);
        _logger.Information(
            "Mod validation completed: {ErrorCount} errors, {WarningCount} warnings",
            errorCount,
            warningCount
        );

        return issues;
    }

    private void ValidateManifest(
        ModManifest manifest,
        string filePath,
        List<ValidationIssue> issues
    )
    {
        if (string.IsNullOrEmpty(manifest.Id))
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = "Missing or empty 'id' field",
                    ModId = string.Empty,
                    FilePath = filePath,
                }
            );

        if (string.IsNullOrEmpty(manifest.Name))
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = "Missing or empty 'name' field",
                    ModId = manifest.Id,
                    FilePath = filePath,
                }
            );

        if (string.IsNullOrEmpty(manifest.Version))
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Warning,
                    Message = "Missing or empty 'version' field",
                    ModId = manifest.Id,
                    FilePath = filePath,
                }
            );
    }

    private void CollectDefinitionIds(
        ModManifest manifest,
        IModSource modSource,
        Dictionary<string, List<DefinitionLocation>> definitionIds,
        List<ValidationIssue> issues
    )
    {
        if (modSource == null)
        {
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Mod '{manifest.Id}' has no ModSource",
                    ModId = manifest.Id,
                    FilePath = string.Empty,
                }
            );
            return;
        }

        // Enumerate all JSON files using convention-based discovery (no contentFolders needed)
        var jsonFiles = modSource.EnumerateFiles("*.json", SearchOption.AllDirectories);

        foreach (var jsonFile in jsonFiles)
        {
            // Skip mod.json itself
            if (jsonFile.Equals("mod.json", StringComparison.OrdinalIgnoreCase))
                continue;

            ProcessDefinitionFileForValidation(
                jsonFile,
                manifest,
                modSource,
                definitionIds,
                issues
            );
        }
    }

    /// <summary>
    ///     Processes a single definition file for validation: collects definition IDs and validates shader definitions.
    /// </summary>
    /// <param name="jsonFile">The JSON file path to process.</param>
    /// <param name="manifest">The mod manifest.</param>
    /// <param name="modSource">The mod source.</param>
    /// <param name="definitionIds">Dictionary to collect definition IDs into.</param>
    /// <param name="issues">List to add validation issues to.</param>
    private void ProcessDefinitionFileForValidation(
        string jsonFile,
        ModManifest manifest,
        IModSource modSource,
        Dictionary<string, List<DefinitionLocation>> definitionIds,
        List<ValidationIssue> issues
    )
    {
        try
        {
            var jsonContent = modSource.ReadTextFile(jsonFile);
            using var jsonDoc = JsonDocument.Parse(jsonContent);

            CollectDefinitionId(jsonFile, manifest, jsonDoc, definitionIds);
            ValidateShaderDefinitionIfApplicable(jsonFile, jsonDoc, modSource, issues);
        }
        catch (JsonException ex)
        {
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Invalid JSON in definition file: {ex.Message}",
                    ModId = manifest.Id,
                    FilePath = jsonFile,
                }
            );
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Unexpected error validating definition file {FilePath} in mod {ModId}",
                jsonFile,
                manifest.Id
            );
            // Continue validation for other files
        }
    }

    /// <summary>
    ///     Collects a definition ID from a JSON document.
    /// </summary>
    /// <param name="jsonFile">The JSON file path.</param>
    /// <param name="manifest">The mod manifest.</param>
    /// <param name="jsonDoc">The parsed JSON document.</param>
    /// <param name="definitionIds">Dictionary to add the definition ID to.</param>
    private void CollectDefinitionId(
        string jsonFile,
        ModManifest manifest,
        JsonDocument jsonDoc,
        Dictionary<string, List<DefinitionLocation>> definitionIds
    )
    {
        if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
        {
            var id = idElement.GetString();
            if (!string.IsNullOrEmpty(id))
            {
                if (!definitionIds.ContainsKey(id))
                    definitionIds[id] = new List<DefinitionLocation>();

                definitionIds[id]
                    .Add(
                        new DefinitionLocation
                        {
                            ModId = manifest.Id,
                            FilePath = jsonFile, // Store relative path
                        }
                    );
            }
        }
    }

    /// <summary>
    ///     Validates shader definitions if the file path indicates it's a shader definition.
    /// </summary>
    /// <param name="jsonFile">The JSON file path.</param>
    /// <param name="jsonDoc">The parsed JSON document.</param>
    /// <param name="modSource">The mod source.</param>
    /// <param name="issues">List to add validation issues to.</param>
    private void ValidateShaderDefinitionIfApplicable(
        string jsonFile,
        JsonDocument jsonDoc,
        IModSource modSource,
        List<ValidationIssue> issues
    )
    {
        // Validate shader definitions (check path instead of folderType)
        var normalizedPath = ModPathNormalizer.Normalize(jsonFile);
        if (
            normalizedPath.StartsWith(
                "definitions/assets/shaders/",
                StringComparison.OrdinalIgnoreCase
            )
        )
            ValidateShaderDefinition(jsonFile, jsonDoc, modSource, issues);
    }

    /// <summary>
    ///     Recursively checks for circular dependencies starting from a mod.
    /// </summary>
    /// <param name="modId">The mod ID to check.</param>
    /// <param name="manifest">The mod manifest.</param>
    /// <param name="allMods">All available mod manifests.</param>
    /// <param name="visited">Set of mod IDs that have been visited in the current path.</param>
    /// <param name="issues">List to add validation issues to.</param>
    private void CheckCircularDependencies(
        string modId,
        ModManifest manifest,
        Dictionary<string, ModManifest> allMods,
        HashSet<string> visited,
        List<ValidationIssue> issues
    )
    {
        if (visited.Contains(modId))
        {
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Circular dependency detected involving mod '{modId}'",
                    ModId = modId,
                    FilePath = string.Empty,
                }
            );
            return;
        }

        visited.Add(modId);

        foreach (var depId in manifest.Dependencies)
            if (allMods.TryGetValue(depId, out var depManifest))
                CheckCircularDependencies(
                    depId,
                    depManifest,
                    allMods,
                    new HashSet<string>(visited),
                    issues
                );
    }

    /// <summary>
    ///     Validates a shader definition file.
    /// </summary>
    /// <param name="definitionPath">Path to the shader definition JSON file (relative).</param>
    /// <param name="jsonDoc">The parsed JSON document.</param>
    /// <param name="modSource">The mod source.</param>
    /// <param name="issues">List to add validation issues to.</param>
    private void ValidateShaderDefinition(
        string definitionPath,
        JsonDocument jsonDoc,
        IModSource modSource,
        List<ValidationIssue> issues
    )
    {
        var root = jsonDoc.RootElement;

        // Validate required fields
        if (
            !root.TryGetProperty("id", out var idElement)
            || string.IsNullOrEmpty(idElement.GetString())
        )
        {
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = "Shader definition missing required 'id' field",
                    ModId = string.Empty,
                    FilePath = definitionPath,
                }
            );
            return;
        }

        var id = idElement.GetString()!;

        if (
            !root.TryGetProperty("name", out var nameElement)
            || string.IsNullOrEmpty(nameElement.GetString())
        )
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Shader definition '{id}' missing required 'name' field",
                    ModId = string.Empty,
                    FilePath = definitionPath,
                }
            );

        if (
            !root.TryGetProperty("sourceFile", out var sourceFileElement)
            || string.IsNullOrEmpty(sourceFileElement.GetString())
        )
        {
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Shader definition '{id}' missing required 'sourceFile' field",
                    ModId = string.Empty,
                    FilePath = definitionPath,
                }
            );
            return;
        }

        var sourceFile = sourceFileElement.GetString()!;

        // ID format validation removed - IDs are for convenience, not enforced

        // Validate sourceFile has .mgfxo extension
        if (!sourceFile.EndsWith(".mgfxo", StringComparison.OrdinalIgnoreCase))
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Shader sourceFile '{sourceFile}' must have .mgfxo extension",
                    ModId = string.Empty,
                    FilePath = definitionPath,
                }
            );

        // Validate .mgfxo file exists relative to mod root
        if (!modSource.FileExists(sourceFile))
            issues.Add(
                new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message =
                        $"Shader compiled file not found: {sourceFile}. Ensure shaders are compiled during build.",
                    ModId = string.Empty,
                    FilePath = definitionPath,
                }
            );
    }

    private class DefinitionLocation
    {
        public string ModId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }
}
