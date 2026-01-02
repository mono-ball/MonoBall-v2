using System.Text.Json;
using System.Text.Json.Serialization;

namespace Porycon3.Services;

/// <summary>
/// Generates mod.json manifest files for converted content.
/// </summary>
public class ManifestGenerator
{
    private readonly string _outputPath;
    private readonly string _namespace;
    private readonly string _region;

    public ManifestGenerator(string outputPath, string @namespace, string region)
    {
        _outputPath = outputPath;
        _namespace = @namespace;
        _region = region;
    }

    /// <summary>
    /// Generate a mod.json manifest based on the converted content.
    /// </summary>
    public void Generate(string? modName = null, string? author = null, string? version = null, string? description = null)
    {
        var manifest = new ModManifest
        {
            Id = $"{_namespace}:{_region}",
            Name = modName ?? $"Pokemon {ToPascalCase(_region)}",
            Author = author ?? "Porycon3",
            Version = version ?? "1.0.0",
            Description = description ?? $"Converted content from pokeemerald-expansion ({_region} region)",
            Dependencies = new List<string> { "base:monoball-core" }
        };

        var manifestPath = Path.Combine(_outputPath, "mod.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        File.WriteAllText(manifestPath, json);
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var parts = input.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join("", parts.Select(p =>
            char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
    }

    /// <summary>
    /// Internal model for mod.json serialization.
    /// </summary>
    private class ModManifest
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Author { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Patches { get; set; } = new();
        public List<string> Dependencies { get; set; } = new();
    }
}
