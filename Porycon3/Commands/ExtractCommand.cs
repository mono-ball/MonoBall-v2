using Spectre.Console;
using Spectre.Console.Cli;
using Porycon3.Services;

namespace Porycon3.Commands;

public class ExtractCommand : Command<ExtractSettings>
{
    public override int Execute(CommandContext context, ExtractSettings settings)
    {
        try
        {
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

                default:
                    AnsiConsole.MarkupLine($"[red]Unknown asset type: {settings.Asset}[/]");
                    AnsiConsole.MarkupLine("Available: doors, fieldeffects, fonts, interface");
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
}
