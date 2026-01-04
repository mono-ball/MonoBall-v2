using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Porycon3.Commands;

public class ConvertSettings : CommandSettings
{
    [CommandArgument(0, "<INPUT>")]
    [Description("Input directory (pokeemerald-expansion root)")]
    public string InputPath { get; set; } = "";

    [CommandArgument(1, "<OUTPUT>")]
    [Description("Output directory for converted files")]
    public string OutputPath { get; set; } = "";

    [CommandOption("-m|--map <MAP>")]
    [Description("Convert a single map by name (e.g., Route101)")]
    public string? MapName { get; set; }

    [CommandOption("-r|--region <REGION>")]
    [Description("Region name for output organization (default: hoenn)")]
    [DefaultValue("hoenn")]
    public string Region { get; set; } = "hoenn";

    /// <summary>
    /// Convert region to PascalCase for use in IDs.
    /// </summary>
    public string RegionPascalCase => ToPascalCase(Region);

    private static string ToPascalCase(string input) => Services.IdTransformer.ToPascalCase(input);

    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output format: tiled or entity (default: entity)")]
    [DefaultValue("entity")]
    public string Format { get; set; } = "entity";

    [CommandOption("-p|--parallel <COUNT>")]
    [Description("Maximum parallel conversions (default: CPU count)")]
    public int? Parallelism { get; set; }

    [CommandOption("-v|--verbose")]
    [Description("Show detailed progress information")]
    public bool Verbose { get; set; }

    [CommandOption("--debug")]
    [Description("Show debug information")]
    public bool Debug { get; set; }

    [CommandOption("-n|--namespace <NAMESPACE>")]
    [Description("ID namespace/prefix for generated definitions (required)")]
    public string Namespace { get; set; } = "";

    [CommandOption("--mod-name <NAME>")]
    [Description("Mod display name for manifest (default: Pokemon {Region})")]
    public string? ModName { get; set; }

    [CommandOption("--mod-author <AUTHOR>")]
    [Description("Mod author for manifest (default: Porycon3)")]
    public string? ModAuthor { get; set; }

    [CommandOption("--mod-version <VERSION>")]
    [Description("Mod version for manifest (default: 1.0.0)")]
    public string? ModVersion { get; set; }

    [CommandOption("--no-manifest")]
    [Description("Skip generating mod.json manifest")]
    public bool NoManifest { get; set; }

    // Extractor selection options
    [CommandOption("--only <EXTRACTORS>")]
    [Description("Only run specified extractors (comma-separated: maps,sprites,sound,pokemon,scripts,etc.)")]
    public string? OnlyExtractors { get; set; }

    [CommandOption("--skip <EXTRACTORS>")]
    [Description("Skip specified extractors (comma-separated: sound,pokemon,sprites,etc.)")]
    public string? SkipExtractors { get; set; }

    // Convenience flags for common scenarios
    [CommandOption("--no-sound")]
    [Description("Skip sound extraction (music, SFX, cries)")]
    public bool NoSound { get; set; }

    [CommandOption("--no-pokemon")]
    [Description("Skip Pokemon sprite and species extraction")]
    public bool NoPokemon { get; set; }

    [CommandOption("--no-sprites")]
    [Description("Skip NPC/overworld sprite extraction")]
    public bool NoSprites { get; set; }

    [CommandOption("--maps-only")]
    [Description("Only convert maps and tilesets (skip all asset extraction)")]
    public bool MapsOnly { get; set; }

    [CommandOption("--assets-only")]
    [Description("Only extract assets (skip map conversion)")]
    public bool AssetsOnly { get; set; }

    /// <summary>
    /// Get the set of extractors to skip based on command-line options.
    /// </summary>
    public HashSet<string> GetSkippedExtractors()
    {
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add convenience flag skips
        if (NoSound) skipped.Add("sound");
        if (NoPokemon)
        {
            skipped.Add("pokemon");
            skipped.Add("species");
        }
        if (NoSprites) skipped.Add("sprites");

        // Add explicit skip list
        if (!string.IsNullOrEmpty(SkipExtractors))
        {
            foreach (var name in SkipExtractors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                skipped.Add(name);
            }
        }

        // Maps-only mode skips all extractors
        if (MapsOnly)
        {
            skipped.Add("*"); // Special marker for "skip all"
        }

        return skipped;
    }

    /// <summary>
    /// Get the set of extractors to include (null = all).
    /// </summary>
    public HashSet<string>? GetOnlyExtractors()
    {
        if (string.IsNullOrEmpty(OnlyExtractors))
            return null;

        var only = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in OnlyExtractors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            only.Add(name);
        }
        return only;
    }

    public override ValidationResult Validate()
    {
        if (string.IsNullOrWhiteSpace(InputPath))
            return ValidationResult.Error("Input path is required");

        if (!Directory.Exists(InputPath))
            return ValidationResult.Error($"Input directory not found: {InputPath}");

        if (string.IsNullOrWhiteSpace(OutputPath))
            return ValidationResult.Error("Output path is required");

        if (Format != "tiled" && Format != "entity")
            return ValidationResult.Error("Format must be 'tiled' or 'entity'");

        return ValidationResult.Success();
    }
}
