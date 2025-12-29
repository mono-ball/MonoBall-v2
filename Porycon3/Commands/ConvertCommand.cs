using Spectre.Console;
using Spectre.Console.Cli;
using Porycon3.Services;
using Porycon3.Models;

namespace Porycon3.Commands;

public class ConvertCommand : Command<ConvertSettings>
{
    public override int Execute(CommandContext context, ConvertSettings settings)
    {
        // Display header
        AnsiConsole.Write(new FigletText("Porycon3")
            .Centered()
            .Color(Color.Cyan1));

        AnsiConsole.MarkupLine("[grey]Pokemon Map Converter for pokeemerald-expansion[/]");
        AnsiConsole.WriteLine();

        try
        {
            // Set the ID namespace from command line
            IdTransformer.Namespace = settings.Namespace;

            // Ensure output directory exists
            Directory.CreateDirectory(settings.OutputPath);

            var converter = new MapConversionService(
                settings.InputPath,
                settings.OutputPath,
                settings.Region,
                settings.Verbose);

            if (!string.IsNullOrEmpty(settings.MapName))
            {
                // Convert single map
                ConvertSingleMap(converter, settings);
            }
            else
            {
                // Convert all maps
                ConvertAllMaps(converter, settings);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Conversion complete![/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return 1;
        }
    }

    private void ConvertSingleMap(MapConversionService converter, ConvertSettings settings)
    {
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start($"Converting [cyan]{settings.MapName}[/]...", ctx =>
            {
                var result = converter.ConvertMap(settings.MapName!);

                if (result.Success)
                {
                    AnsiConsole.MarkupLine($"[green]OK[/] Converted {settings.MapName} in {result.Duration.TotalMilliseconds:F0}ms");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]FAIL[/] {result.Error}");
                }
            });

        // Finalize shared tilesets
        var sharedTilesets = converter.FinalizeSharedTilesets();
        AnsiConsole.MarkupLine($"[blue]Generated {sharedTilesets} shared tileset(s)[/]");

        // Generate additional definitions (Weather, BattleScenes, Region, Graphics)
        converter.GenerateDefinitions();
    }

    private void ConvertAllMaps(MapConversionService converter, ConvertSettings settings)
    {
        // Scan for maps
        var maps = converter.ScanMaps();

        AnsiConsole.MarkupLine($"Found [cyan]{maps.Count}[/] maps to convert");
        AnsiConsole.WriteLine();

        var results = new List<ConversionResult>();

        AnsiConsole.Progress()
            .AutoRefresh(true)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .Start(ctx =>
            {
                var task = ctx.AddTask("[cyan]Converting maps[/]", maxValue: maps.Count);

                var parallelism = settings.Parallelism ?? Environment.ProcessorCount;

                Parallel.ForEach(
                    maps,
                    new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                    mapName =>
                    {
                        var result = converter.ConvertMap(mapName);
                        lock (results)
                        {
                            results.Add(result);
                        }
                        task.Increment(1);
                    });
            });

        // Finalize shared tilesets (after all maps processed)
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .Start("Finalizing shared tilesets...", ctx =>
            {
                var sharedTilesets = converter.FinalizeSharedTilesets();
                AnsiConsole.MarkupLine($"[blue]Generated {sharedTilesets} shared tileset(s)[/]");
            });

        // Generate additional definitions (Weather, BattleScenes, Region)
        var definitions = converter.GenerateDefinitions();

        // Display summary table
        DisplaySummary(results, definitions);
    }

    private void DisplaySummary(List<ConversionResult> results, (int Weather, int BattleScenes, bool Region, int Sections, int Themes, int PopupBackgrounds, int PopupOutlines, int WeatherGraphics, int BattleEnvironments, int Sprites, int TextWindows, int Pokemon, int PokemonSprites, int Species, int SpeciesForms, int FieldEffects, int DoorAnimations, int Behaviors, int Scripts) definitions = default)
    {
        var successful = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);
        var totalTime = results.Sum(r => r.Duration.TotalMilliseconds);

        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Total Maps", results.Count.ToString());
        table.AddRow("[green]Successful[/]", successful.ToString());
        table.AddRow("[red]Failed[/]", failed.ToString());
        table.AddRow("Total Time", $"{totalTime / 1000:F1}s");
        if (results.Count > 0)
            table.AddRow("Avg per Map", $"{totalTime / results.Count:F0}ms");

        // Show definition counts
        var hasDefinitions = definitions.Weather > 0 || definitions.BattleScenes > 0 ||
                            definitions.Region || definitions.Sections > 0 || definitions.Themes > 0 ||
                            definitions.PopupBackgrounds > 0 || definitions.PopupOutlines > 0 ||
                            definitions.WeatherGraphics > 0 || definitions.BattleEnvironments > 0 ||
                            definitions.Sprites > 0 || definitions.TextWindows > 0 ||
                            definitions.Pokemon > 0 || definitions.Species > 0 ||
                            definitions.FieldEffects > 0 || definitions.DoorAnimations > 0 ||
                            definitions.Behaviors > 0 || definitions.Scripts > 0;
        if (hasDefinitions)
        {
            table.AddEmptyRow();
            table.AddRow("[blue]Weather Definitions[/]", definitions.Weather.ToString());
            table.AddRow("[blue]Weather Graphics[/]", definitions.WeatherGraphics.ToString());
            table.AddRow("[blue]Battle Scene Definitions[/]", definitions.BattleScenes.ToString());
            table.AddRow("[blue]Battle Environments[/]", definitions.BattleEnvironments.ToString());
            table.AddRow("[blue]NPC/Player Sprites[/]", definitions.Sprites.ToString());
            table.AddRow("[blue]Pokemon[/]", definitions.Pokemon.ToString());
            table.AddRow("[blue]Pokemon Sprites[/]", definitions.PokemonSprites.ToString());
            table.AddRow("[blue]Species Definitions[/]", definitions.Species.ToString());
            table.AddRow("[blue]Species Forms[/]", definitions.SpeciesForms.ToString());
            table.AddRow("[blue]Region Definition[/]", definitions.Region ? "1" : "0");
            table.AddRow("[blue]Map Sections[/]", definitions.Sections.ToString());
            table.AddRow("[blue]Popup Themes[/]", definitions.Themes.ToString());
            table.AddRow("[blue]Popup Backgrounds[/]", definitions.PopupBackgrounds.ToString());
            table.AddRow("[blue]Popup Outlines[/]", definitions.PopupOutlines.ToString());
            table.AddRow("[blue]Text Windows[/]", definitions.TextWindows.ToString());
            table.AddRow("[blue]Field Effects[/]", definitions.FieldEffects.ToString());
            table.AddRow("[blue]Door Animations[/]", definitions.DoorAnimations.ToString());
            table.AddRow("[blue]Behavior Definitions[/]", definitions.Behaviors.ToString());
            table.AddRow("[blue]Script Definitions[/]", definitions.Scripts.ToString());
        }

        AnsiConsole.Write(table);

        // Show failed maps if any
        if (failed > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Failed maps:[/]");
            foreach (var result in results.Where(r => !r.Success).Take(10))
            {
                AnsiConsole.MarkupLine($"  [grey]-[/] {result.MapId}: {result.Error}");
            }
            if (failed > 10)
            {
                AnsiConsole.MarkupLine($"  [grey]... and {failed - 10} more[/]");
            }
        }
    }
}
