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
            Priority = 10, // After base mod
            ContentFolders = DiscoverContentFolders(),
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

    /// <summary>
    /// Discover content folders that exist in the output directory.
    /// Maps to extractor outputs, not individual definition folders.
    /// </summary>
    private Dictionary<string, string> DiscoverContentFolders()
    {
        var folders = new Dictionary<string, string>
        {
            ["Root"] = ""
        };

        var regionPascal = ToPascalCase(_region);

        // Content folder mappings organized by extractor output
        // The ModLoader will recursively load all JSON files under each path
        var mappings = new (string Key, string Path)[]
        {
            // Top-level asset folders
            ("Graphics", "Graphics"),
            ("Audio", "Audio"),
            ("Scripts", "Scripts"),

            // Map conversion output
            ("MapDefinitions", $"Definitions/Entities/Maps/{regionPascal}"),
            ("TilesetDefinitions", $"Definitions/Assets/Tilesets/{regionPascal}"),

            // Script & Behavior extractors
            ("ScriptDefinitions", "Definitions/Scripts"),
            ("BehaviorDefinitions", "Definitions/Behaviors"),

            // Sprite extractor (all NPC/overworld sprites)
            ("SpriteDefinitions", "Definitions/Sprites"),

            // Pokemon extractor (sprites + species)
            ("PokemonAssetDefinitions", "Definitions/Assets/Pokemon"),
            ("PokemonDefinitions", "Definitions/Entities/Pokemon"),

            // Sound extractor
            ("AudioDefinitions", "Definitions/Assets/Audio"),

            // Definition generator outputs
            ("RegionDefinitions", "Definitions/Entities/Regions"),
            ("WeatherDefinitions", "Definitions/Entities/Weather"),
            ("BattleSceneDefinitions", "Definitions/Entities/BattleScenes"),

            // Map section extractor
            ("MapSectionDefinitions", "Definitions/Maps/Sections"),

            // Popup extractor
            ("PopupDefinitions", "Definitions/Maps/Popups"),

            // Text window extractor
            ("TextWindowDefinitions", "Definitions/TextWindow"),

            // Battle environment extractor
            ("BattleAssetDefinitions", "Definitions/Assets/Battle"),

            // Field effect extractor
            ("FieldEffectDefinitions", "Definitions/FieldEffects"),

            // Door animation extractor
            ("DoorAnimationDefinitions", "Definitions/DoorAnimations"),

            // Tile behavior extractor
            ("TileBehaviorDefinitions", "Definitions/TileBehaviors")
        };

        foreach (var (key, relativePath) in mappings)
        {
            var fullPath = Path.Combine(_outputPath, relativePath);
            if (Directory.Exists(fullPath) && DirectoryHasContent(fullPath))
            {
                folders[key] = relativePath;
            }
        }

        return folders;
    }

    /// <summary>
    /// Check if a directory has any files (recursively).
    /// </summary>
    private bool DirectoryHasContent(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
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
        public int Priority { get; set; }
        public Dictionary<string, string> ContentFolders { get; set; } = new();
        public List<string> Patches { get; set; } = new();
        public List<string> Dependencies { get; set; } = new();
    }
}
