using System.Diagnostics;
using Spectre.Console;
using Porycon3.Models;
using Porycon3.Services;
using Porycon3.Services.Progress;
using Porycon3.Services.Extraction;

namespace Porycon3.Commands;

/// <summary>
/// Unified command executor using Spectre.Console Progress for the conversion pipeline.
/// Provides immediate feedback with auto-refreshing progress bars.
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
        // Display header immediately (before any processing)
        AnsiConsole.Write(new FigletText("Porycon3")
            .Centered()
            .Color(Color.Cyan1));
        AnsiConsole.MarkupLine("[grey]Pokemon Map Converter for pokeemerald-expansion[/]");
        AnsiConsole.WriteLine();

        // Force flush to ensure header displays immediately
        Console.Out.Flush();

        try
        {
            // Set the ID namespace from command line
            IdTransformer.Namespace = _settings.Namespace;

            // Ensure output directory exists
            Directory.CreateDirectory(_settings.OutputPath);

            // Initialize converter (fast - just creates lightweight service objects)
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
                return ExecuteAllMapsWithUnifiedDisplay(converter);
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
    /// Execute single map conversion (simple status display).
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

        // Generate definitions using progress-aware orchestrator
        RunDefinitionsWithProgress(converter);

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Conversion complete![/]");
        return 0;
    }

    /// <summary>
    /// Execute full conversion with unified Progress display.
    /// Single Spectre context for all phases - starts immediately for instant feedback.
    /// </summary>
    private int ExecuteAllMapsWithUnifiedDisplay(MapConversionService converter)
    {
        var results = new List<ConversionResult>();
        List<string>? maps = null;

        // Use Progress context which handles rendering automatically
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
                // Create progress tasks for each phase
                var scanTask = ctx.AddTask("[cyan]Scanning maps[/]", autoStart: false);
                var convertTask = ctx.AddTask("[blue]Converting maps[/]", autoStart: false);
                var tilesetTask = ctx.AddTask("[magenta]Finalizing tilesets[/]", autoStart: false);
                var extractTask = ctx.AddTask("[green]Extracting assets[/]", autoStart: false);

                // Phase 0: Scan for maps
                scanTask.StartTask();
                scanTask.IsIndeterminate = true;
                _progress.SetPhase(ConversionPhase.ScanningMaps);

                maps = converter.ScanMaps();
                _progress.SetMapTotal(maps.Count);

                scanTask.IsIndeterminate = false;
                scanTask.MaxValue = 1;
                scanTask.Increment(1);

                if (_cts.IsCancellationRequested) return;

                // Phase 1: Convert maps (parallel)
                convertTask.StartTask();
                convertTask.MaxValue = maps.Count;
                _progress.SetPhase(ConversionPhase.ConvertingMaps);

                ConvertMapsParallelWithTask(converter, maps, results, convertTask);

                if (_cts.IsCancellationRequested) return;

                // Phase 2: Finalize tilesets
                tilesetTask.StartTask();
                tilesetTask.IsIndeterminate = true;
                _progress.SetPhase(ConversionPhase.FinalizingTilesets);

                var tilesetCount = converter.FinalizeSharedTilesets();
                _progress.SetTilesetTotal(tilesetCount);
                _progress.SetTilesetCompleted(tilesetCount);

                tilesetTask.IsIndeterminate = false;
                tilesetTask.MaxValue = 1;
                tilesetTask.Increment(1);

                if (_cts.IsCancellationRequested) return;

                // Phase 3: Extract assets
                extractTask.StartTask();
                _progress.SetPhase(ConversionPhase.ExtractingAssets);

                RunExtractorsWithTask(converter, extractTask);

                // Complete
                _progress.SetPhase(ConversionPhase.Complete);
            });

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
    /// Convert maps in parallel with ProgressTask updates.
    /// </summary>
    private void ConvertMapsParallelWithTask(
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
            // Parallel loop was cancelled, that's fine
        }
    }

    /// <summary>
    /// Run extractors with ProgressTask updates.
    /// </summary>
    private void RunExtractorsWithTask(MapConversionService converter, ProgressTask task)
    {
        // First, run the definition generator
        converter.RunDefinitionGenerator();

        var extractors = converter.GetExtractors().ToList();
        task.MaxValue = extractors.Count;

        foreach (var extractor in extractors)
        {
            if (_cts.IsCancellationRequested) break;

            task.Description = $"[green]Extracting:[/] {extractor.Name}";
            _progress.RegisterExtractor(extractor.Name);
            _progress.UpdateExtractor(extractor.Name, ExtractorState.Running);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                extractor.QuietMode = true;
                var result = extractor.ExtractAll();
                sw.Stop();

                var state = result.Success ? ExtractorState.Complete : ExtractorState.Failed;
                _progress.UpdateExtractor(extractor.Name, state, result.ItemCount, sw.Elapsed);
            }
            catch (Exception)
            {
                sw.Stop();
                _progress.UpdateExtractor(extractor.Name, ExtractorState.Failed, elapsed: sw.Elapsed);
            }

            task.Increment(1);
        }

        task.Description = "[green]Extracting assets[/]";
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

    /// <summary>
    /// Run definitions for single map mode.
    /// </summary>
    private void RunDefinitionsWithProgress(MapConversionService converter)
    {
        _progress.SetPhase(ConversionPhase.ExtractingAssets);

        AnsiConsole.Progress()
            .AutoClear(false)
            .Start(ctx =>
            {
                var task = ctx.AddTask("[green]Extracting assets[/]");
                RunExtractorsWithTask(converter, task);
            });

        _progress.SetPhase(ConversionPhase.Complete);
        ShowCompletionSummary(new List<ConversionResult>());
    }

}
