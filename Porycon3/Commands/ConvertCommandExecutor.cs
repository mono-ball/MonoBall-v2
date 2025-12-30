using System.Diagnostics;
using Spectre.Console;
using Porycon3.Models;
using Porycon3.Services;
using Porycon3.Services.Progress;
using Porycon3.Services.Extraction;

namespace Porycon3.Commands;

/// <summary>
/// Unified command executor using Spectre.Console Progress for the conversion pipeline.
/// </summary>
public class ConvertCommandExecutor
{
    private readonly ConvertSettings _settings;
    private readonly ConversionProgress _progress;
    private readonly CancellationTokenSource _cts;

    public ConvertCommandExecutor(ConvertSettings settings)
    {
        _settings = settings;
        _progress = new ConversionProgress();
        _cts = new CancellationTokenSource();

        // Handle Ctrl+C gracefully
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
            _progress.SetPhase(ConversionPhase.Failed);
        };
    }

    /// <summary>
    /// Execute the full conversion pipeline with unified progress display.
    /// </summary>
    public int Execute()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch
        {
            // Ignore encoding errors
        }

        AnsiConsole.Write(new FigletText("Porycon3")
            .LeftJustified()
            .Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]Pokemon Map Converter for pokeemerald-expansion[/]");
        AnsiConsole.WriteLine();

        try
        {
            // Set the ID namespace from command line
            IdTransformer.Namespace = _settings.Namespace;

            // Ensure output directory exists
            Directory.CreateDirectory(_settings.OutputPath);

            // Initialize converter
            AnsiConsole.MarkupLine("[dim]Initializing...[/]");

            var sw = Stopwatch.StartNew();
            var converter = new MapConversionService(
                _settings.InputPath,
                _settings.OutputPath,
                _settings.Region,
                _settings.Verbose);

            if (_settings.Verbose)
                AnsiConsole.MarkupLine($"[dim]Initialization: {sw.ElapsedMilliseconds}ms[/]");

            if (!string.IsNullOrEmpty(_settings.MapName))
            {
                return ExecuteSingleMap(converter);
            }
            else
            {
                return ExecuteAllMaps(converter);
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Conversion cancelled by user.[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            return 1;
        }
    }

    /// <summary>
    /// Execute single map conversion.
    /// </summary>
    private int ExecuteSingleMap(MapConversionService converter)
    {
        var result = converter.ConvertMap(_settings.MapName!);

        if (result.Success)
        {
            AnsiConsole.MarkupLine($"[green]OK[/] Converted {_settings.MapName} in {result.Duration.TotalMilliseconds:F0}ms");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]FAIL[/] {result.Error}");
            return 1;
        }

        // Finalize shared tilesets
        var sharedTilesetCount = converter.FinalizeSharedTilesets();
        AnsiConsole.MarkupLine($"[blue]Generated {sharedTilesetCount} shared tileset(s)[/]");

        // Run extractors
        RunExtractors(converter);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Conversion complete![/]");
        return 0;
    }

    /// <summary>
    /// Execute full conversion with phased display.
    /// </summary>
    private int ExecuteAllMaps(MapConversionService converter)
    {
        var results = new List<ConversionResult>();

        // Phase 0: Scan for maps
        AnsiConsole.Markup("[cyan]Scanning maps...[/] ");
        _progress.SetPhase(ConversionPhase.ScanningMaps);

        var maps = converter.ScanMaps();
        _progress.SetMapTotal(maps.Count);
        AnsiConsole.MarkupLine($"[green]found {maps.Count} maps[/]");

        if (_cts.IsCancellationRequested) return 1;

        // Phase 1: Convert maps (parallel)
        _progress.SetPhase(ConversionPhase.ConvertingMaps);
        AnsiConsole.Progress()
            .AutoClear(false)
            .AutoRefresh(true)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .Start(ctx =>
            {
                var convertTask = ctx.AddTask("[blue]Converting maps[/]", maxValue: maps.Count);
                ConvertMapsParallel(converter, maps, results, convertTask);
            });

        if (_cts.IsCancellationRequested) return 1;

        // Phase 2: Finalize tilesets
        AnsiConsole.Markup("[magenta]Finalizing tilesets...[/] ");
        _progress.SetPhase(ConversionPhase.FinalizingTilesets);

        var tilesetCount = converter.FinalizeSharedTilesets();
        _progress.SetTilesetTotal(tilesetCount);
        _progress.SetTilesetCompleted(tilesetCount);
        AnsiConsole.MarkupLine($"[green]{tilesetCount} shared tilesets[/]");

        if (_cts.IsCancellationRequested) return 1;

        // Phase 3: Extract assets (sequential)
        _progress.SetPhase(ConversionPhase.ExtractingAssets);
        AnsiConsole.MarkupLine("[yellow]Extracting assets...[/]");

        AnsiConsole.Progress()
            .AutoClear(false)
            .AutoRefresh(true)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn())
            .Start(ctx => RunExtractorsWithProgress(converter, ctx));

        // Complete
        _progress.SetPhase(ConversionPhase.Complete);

        // Show summary
        ShowCompletionSummary(results);

        // Show detailed failed map errors if any
        var failed = results.Count(r => !r.Success);
        if (failed > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Failed Map Details:[/]");

            foreach (var result in results.Where(r => !r.Success).Take(10))
            {
                AnsiConsole.MarkupLine($"  [dim]•[/] [yellow]{result.MapId}[/]: {Markup.Escape(result.Error ?? "Unknown error")}");
            }

            if (failed > 10)
            {
                AnsiConsole.MarkupLine($"  [dim]... and {failed - 10} more[/]");
            }
        }

        return 0;
    }

    /// <summary>
    /// Convert maps in parallel.
    /// </summary>
    private void ConvertMapsParallel(
        MapConversionService converter,
        List<string> maps,
        List<ConversionResult> results,
        ProgressTask task)
    {
        var parallelism = _settings.Parallelism ?? Environment.ProcessorCount;
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = _cts.Token
        };

        try
        {
            Parallel.ForEach(maps, options, mapName =>
            {
                var result = converter.ConvertMap(mapName);
                lock (results)
                {
                    results.Add(result);
                    task.Increment(1);
                }
                _progress.IncrementMapCompleted(mapName, result.Success);
            });
        }
        catch (OperationCanceledException)
        {
            // Cancelled, that's fine
        }
    }

    /// <summary>
    /// Run extractors in parallel with progress display.
    /// </summary>
    private void RunExtractorsWithProgress(MapConversionService converter, ProgressContext ctx)
    {
        // First, run the definition generator (must complete before extractors)
        var defTask = ctx.AddTask("[dim]Generating definitions[/]", maxValue: 1);
        converter.RunDefinitionGenerator();
        defTask.Increment(1);

        var extractors = converter.GetExtractors().ToList();

        // Create a progress task for each extractor
        var tasks = extractors.Select(e =>
        {
            var task = ctx.AddTask($"[dim]{e.Name}[/]", autoStart: true, maxValue: 1);
            _progress.RegisterExtractor(e.Name);
            return (Extractor: e, Task: task);
        }).ToList();

        // Run all extractors in parallel
        Parallel.ForEach(tasks, new ParallelOptions { CancellationToken = _cts.Token }, item =>
        {
            var (extractor, task) = item;

            if (_cts.IsCancellationRequested)
            {
                _progress.UpdateExtractor(extractor.Name, ExtractorState.Skipped);
                task.Description = $"[yellow]{extractor.Name}[/] [dim](skipped)[/]";
                task.Increment(1);
                return;
            }

            task.Description = $"[blue]{extractor.Name}[/]";
            _progress.UpdateExtractor(extractor.Name, ExtractorState.Running);

            var sw = Stopwatch.StartNew();
            try
            {
                extractor.QuietMode = true;
                var result = extractor.ExtractAll();
                sw.Stop();

                var state = result.Success ? ExtractorState.Complete : ExtractorState.Failed;
                _progress.UpdateExtractor(extractor.Name, state, result.ItemCount, sw.Elapsed);

                task.Description = result.Success
                    ? $"[green]{extractor.Name}[/] [dim]({result.ItemCount})[/]"
                    : $"[red]{extractor.Name}[/] [dim](failed)[/]";
            }
            catch (Exception)
            {
                sw.Stop();
                _progress.UpdateExtractor(extractor.Name, ExtractorState.Failed, elapsed: sw.Elapsed);
                task.Description = $"[red]{extractor.Name}[/] [dim](error)[/]";
            }

            task.Increment(1);
        });
    }

    /// <summary>
    /// Run extractors for single map mode.
    /// </summary>
    private void RunExtractors(MapConversionService converter)
    {
        _progress.SetPhase(ConversionPhase.ExtractingAssets);

        AnsiConsole.Progress()
            .AutoClear(false)
            .AutoRefresh(true)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .Start(ctx => RunExtractorsWithProgress(converter, ctx));

        _progress.SetPhase(ConversionPhase.Complete);
    }

    /// <summary>
    /// Show completion summary.
    /// </summary>
    private void ShowCompletionSummary(List<ConversionResult> results)
    {
        var snapshot = _progress.GetSnapshot();
        var totalItems = snapshot.Extractors.Sum(e => e.ItemCount);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            $"[green bold]✓ Complete[/] │ " +
            $"[green]{snapshot.MapCompleted}[/] maps │ " +
            $"[green]{snapshot.TilesetCompleted}[/] tilesets │ " +
            $"[green]{totalItems:N0}[/] assets │ " +
            $"[dim]{snapshot.TotalElapsed:mm\\:ss\\.fff}[/]");

        if (snapshot.MapFailed > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠[/] [yellow]{snapshot.MapFailed}[/] map(s) failed");
        }

        var extractorsFailed = snapshot.Extractors.Count(e => e.State == ExtractorState.Failed);
        if (extractorsFailed > 0)
        {
            var failedNames = string.Join(", ", snapshot.Extractors
                .Where(e => e.State == ExtractorState.Failed)
                .Select(e => e.Name));
            AnsiConsole.MarkupLine($"[red]✗[/] [red]{extractorsFailed}[/] extractor(s) failed: {failedNames}");
        }
    }
}
