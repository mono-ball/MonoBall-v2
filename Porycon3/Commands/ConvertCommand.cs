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

        // Display summary table
        DisplaySummary(results);
    }

    private void DisplaySummary(List<ConversionResult> results)
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
