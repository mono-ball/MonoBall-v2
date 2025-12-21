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
    /// Validates mod definitions and manifests for inconsistencies.
    /// </summary>
    public class ModValidator
    {
        private readonly string _modsDirectory;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the ModValidator.
        /// </summary>
        /// <param name="modsDirectory">Path to the Mods directory.</param>
        /// <param name="logger">The logger instance for logging validation messages.</param>
        public ModValidator(string modsDirectory, ILogger logger)
        {
            _modsDirectory =
                modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Validates all mods and returns a list of inconsistencies found.
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

            var modDirectories = Directory.GetDirectories(_modsDirectory);
            _logger.Debug("Found {ModCount} mod directories", modDirectories.Length);
            var modManifests = new Dictionary<string, ModManifest>();
            var definitionIds = new Dictionary<string, List<DefinitionLocation>>();

            // First pass: Load all manifests and collect definition IDs
            foreach (var modDir in modDirectories)
            {
                var modJsonPath = Path.Combine(modDir, "mod.json");
                if (!File.Exists(modJsonPath))
                {
                    issues.Add(
                        new ValidationIssue
                        {
                            Severity = ValidationSeverity.Warning,
                            Message =
                                $"Mod directory '{Path.GetFileName(modDir)}' does not contain mod.json",
                            ModId = string.Empty,
                            FilePath = modDir,
                        }
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
                        issues.Add(
                            new ValidationIssue
                            {
                                Severity = ValidationSeverity.Error,
                                Message = $"Failed to deserialize mod.json",
                                ModId = string.Empty,
                                FilePath = modJsonPath,
                            }
                        );
                        continue;
                    }

                    // Validate manifest fields
                    ValidateManifest(manifest, modJsonPath, issues);

                    if (!string.IsNullOrEmpty(manifest.Id))
                    {
                        if (modManifests.ContainsKey(manifest.Id))
                        {
                            issues.Add(
                                new ValidationIssue
                                {
                                    Severity = ValidationSeverity.Error,
                                    Message = $"Duplicate mod ID '{manifest.Id}'",
                                    ModId = manifest.Id,
                                    FilePath = modJsonPath,
                                }
                            );
                        }
                        else
                        {
                            modManifests[manifest.Id] = manifest;
                        }
                    }

                    // Collect definition IDs
                    CollectDefinitionIds(manifest, modDir, definitionIds);
                }
                catch (Exception ex)
                {
                    issues.Add(
                        new ValidationIssue
                        {
                            Severity = ValidationSeverity.Error,
                            Message = $"Error reading mod.json: {ex.Message}",
                            ModId = string.Empty,
                            FilePath = modJsonPath,
                        }
                    );
                }
            }

            // Check for duplicate definition IDs
            foreach (var (id, locations) in definitionIds)
            {
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
            }

            // Check for missing dependencies
            foreach (var (modId, manifest) in modManifests)
            {
                foreach (var depId in manifest.Dependencies)
                {
                    if (!modManifests.ContainsKey(depId))
                    {
                        issues.Add(
                            new ValidationIssue
                            {
                                Severity = ValidationSeverity.Error,
                                Message = $"Mod '{modId}' depends on '{depId}' which is not found",
                                ModId = modId,
                                FilePath = Path.Combine(_modsDirectory, modId, "mod.json"),
                            }
                        );
                    }
                }
            }

            // Check for circular dependencies
            foreach (var (modId, manifest) in modManifests)
            {
                CheckCircularDependencies(
                    modId,
                    manifest,
                    modManifests,
                    new HashSet<string>(),
                    issues
                );
            }

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
            {
                issues.Add(
                    new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = "Missing or empty 'id' field",
                        ModId = string.Empty,
                        FilePath = filePath,
                    }
                );
            }

            if (string.IsNullOrEmpty(manifest.Name))
            {
                issues.Add(
                    new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = "Missing or empty 'name' field",
                        ModId = manifest.Id,
                        FilePath = filePath,
                    }
                );
            }

            if (string.IsNullOrEmpty(manifest.Version))
            {
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
        }

        private void CollectDefinitionIds(
            ModManifest manifest,
            string modDir,
            Dictionary<string, List<DefinitionLocation>> definitionIds
        )
        {
            foreach (var (folderType, relativePath) in manifest.ContentFolders)
            {
                if (string.IsNullOrEmpty(relativePath))
                {
                    continue;
                }

                var definitionsPath = Path.Combine(modDir, relativePath);
                if (!Directory.Exists(definitionsPath))
                {
                    continue;
                }

                var jsonFiles = Directory.GetFiles(
                    definitionsPath,
                    "*.json",
                    SearchOption.AllDirectories
                );
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(jsonFile);
                        var jsonDoc = JsonDocument.Parse(jsonContent);

                        if (jsonDoc.RootElement.TryGetProperty("id", out var idElement))
                        {
                            var id = idElement.GetString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                if (!definitionIds.ContainsKey(id))
                                {
                                    definitionIds[id] = new List<DefinitionLocation>();
                                }

                                definitionIds[id]
                                    .Add(
                                        new DefinitionLocation
                                        {
                                            ModId = manifest.Id,
                                            FilePath = jsonFile,
                                        }
                                    );
                            }
                        }
                    }
                    catch
                    {
                        // Skip invalid JSON files during validation
                    }
                }
            }
        }

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
            {
                if (allMods.TryGetValue(depId, out var depManifest))
                {
                    CheckCircularDependencies(
                        depId,
                        depManifest,
                        allMods,
                        new HashSet<string>(visited),
                        issues
                    );
                }
            }
        }

        private class DefinitionLocation
        {
            public string ModId { get; set; } = string.Empty;
            public string FilePath { get; set; } = string.Empty;
        }
    }
}
