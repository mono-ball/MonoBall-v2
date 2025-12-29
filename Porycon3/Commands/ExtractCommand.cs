using Spectre.Console;
using Spectre.Console.Cli;
using Porycon3.Services;
using Porycon3.Services.Sound;

namespace Porycon3.Commands;

public class ExtractCommand : Command<ExtractSettings>
{
    public override int Execute(CommandContext context, ExtractSettings settings)
    {
        try
        {
            // Set the ID namespace from command line
            IdTransformer.Namespace = settings.Namespace;

            Directory.CreateDirectory(settings.OutputPath);

            switch (settings.Asset.ToLowerInvariant())
            {
                case "doors":
                case "door_anims":
                case "dooranimations":
                    var doorExtractor = new DoorAnimationExtractor(settings.InputPath, settings.OutputPath);
                    var doorCount = doorExtractor.Extract();
                    AnsiConsole.MarkupLine($"[green]Extracted {doorCount} door animations[/]");
                    break;

                case "fieldeffects":
                case "field_effects":
                    var fieldExtractor = new FieldEffectExtractor(settings.InputPath, settings.OutputPath, true);
                    var fieldCount = fieldExtractor.ExtractAll();
                    AnsiConsole.MarkupLine($"[green]Extracted {fieldCount} field effects[/]");
                    break;

                case "fonts":
                    var fontExtractor = new FontExtractor(settings.InputPath, settings.OutputPath);
                    var fontCount = fontExtractor.Extract();
                    AnsiConsole.MarkupLine($"[green]Extracted {fontCount} font assets[/]");
                    break;

                case "interface":
                case "ui":
                    var interfaceExtractor = new InterfaceExtractor(settings.InputPath, settings.OutputPath);
                    var interfaceCount = interfaceExtractor.ExtractAll();
                    AnsiConsole.MarkupLine($"[green]Extracted {interfaceCount} interface graphics[/]");
                    break;

                case "sound":
                case "audio":
                case "music":
                    var soundExtractor = new SoundExtractor(settings.InputPath, settings.OutputPath);
                    soundExtractor.ExtractAll();
                    AnsiConsole.MarkupLine($"[green]Sound extraction complete![/]");
                    break;

                case "behaviors":
                case "behaviour":
                    var behaviorExtractor = new BehaviorExtractor(settings.InputPath, settings.OutputPath, settings.Verbose);
                    var (behaviors, behaviorScripts) = behaviorExtractor.ExtractAll();
                    AnsiConsole.MarkupLine($"[green]Extracted {behaviors} behavior definitions and {behaviorScripts} behavior scripts[/]");
                    break;

                case "scripts":
                    var scriptExtractor = new ScriptExtractor(settings.InputPath, settings.OutputPath, settings.Verbose);
                    var (interactions, triggers, signs, totalScripts) = scriptExtractor.ExtractAll();
                    AnsiConsole.MarkupLine($"[green]Extracted {totalScripts} script definitions ({interactions} interactions, {triggers} triggers, {signs} signs)[/]");
                    break;

                default:
                    AnsiConsole.MarkupLine($"[red]Unknown asset type: {settings.Asset}[/]");
                    AnsiConsole.MarkupLine("Available: doors, fieldeffects, fonts, interface, sound, behaviors, scripts");
                    return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return 1;
        }
    }
}

public class ExtractSettings : CommandSettings
{
    [CommandArgument(0, "<INPUT>")]
    public string InputPath { get; set; } = "";

    [CommandArgument(1, "<OUTPUT>")]
    public string OutputPath { get; set; } = "";

    [CommandArgument(2, "<ASSET>")]
    public string Asset { get; set; } = "";

    [CommandOption("-n|--namespace <NAMESPACE>")]
    [System.ComponentModel.Description("ID namespace/prefix for generated definitions (default: base)")]
    [System.ComponentModel.DefaultValue("base")]
    public string Namespace { get; set; } = "base";

    [CommandOption("-v|--verbose")]
    [System.ComponentModel.Description("Show verbose output")]
    public bool Verbose { get; set; }
}
