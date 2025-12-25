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
