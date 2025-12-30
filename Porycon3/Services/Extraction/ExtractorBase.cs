using System.Diagnostics;
using Spectre.Console;

namespace Porycon3.Services.Extraction;

/// <summary>
/// Base class for all extractors providing consistent Spectre Console output,
/// error handling, and result building.
/// </summary>
public abstract class ExtractorBase : IExtractor
{
    protected readonly string InputPath;
    protected readonly string OutputPath;
    protected readonly bool Verbose;

    private readonly Stopwatch _stopwatch = new();
    private readonly List<ExtractionError> _errors = new();
    private readonly List<string> _warnings = new();
    private readonly Dictionary<string, int> _counts = new();

    /// <summary>
    /// When true, suppresses all console output (for use with orchestrator).
    /// </summary>
    public bool QuietMode { get; set; }

    public abstract string Name { get; }
    public abstract string Description { get; }

    protected ExtractorBase(string inputPath, string outputPath, bool verbose = false)
    {
        InputPath = inputPath ?? throw new ArgumentNullException(nameof(inputPath));
        OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        Verbose = verbose;
    }

    /// <summary>
    /// Execute extraction with standardized Spectre Console output.
    /// </summary>
    public ExtractionResult ExtractAll()
    {
        _stopwatch.Restart();
        _errors.Clear();
        _warnings.Clear();
        _counts.Clear();

        int itemCount = 0;

        try
        {
            itemCount = ExecuteExtraction();
        }
        catch (Exception ex)
        {
            AddError("", $"Fatal error: {ex.Message}", ex);
        }

        _stopwatch.Stop();

        var result = new ExtractionResult
        {
            ExtractorName = Name,
            Success = _errors.Count == 0,
            ItemCount = itemCount,
            AdditionalCounts = new Dictionary<string, int>(_counts),
            Duration = _stopwatch.Elapsed,
            Errors = new List<ExtractionError>(_errors),
            Warnings = new List<string>(_warnings),
            OutputPath = OutputPath
        };

        if (!QuietMode)
            DisplaySummary(result);

        return result;
    }

    /// <summary>
    /// Override to implement the actual extraction logic.
    /// </summary>
    /// <returns>Primary count of items extracted.</returns>
    protected abstract int ExecuteExtraction();

    #region Progress Helpers

    /// <summary>
    /// Safely set progress task description (null-safe for QuietMode).
    /// </summary>
    protected static void SetTaskDescription(ProgressTask? task, string description)
    {
        if (task != null)
            task.Description = description;
    }

    /// <summary>
    /// Run extraction with a progress bar for known item counts.
    /// In quiet mode, just iterates without display.
    /// </summary>
    protected void WithProgress<T>(string description, IReadOnlyList<T> items, Action<T, ProgressTask?> processItem)
    {
        if (QuietMode)
        {
            // Simple iteration without progress display
            foreach (var item in items)
            {
                try
                {
                    processItem(item, null!);
                }
                catch (Exception ex)
                {
                    AddError(item?.ToString() ?? "unknown", ex.Message, ex);
                }
            }
            return;
        }

        AnsiConsole.Progress()
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
                var task = ctx.AddTask($"[cyan]{Markup.Escape(description)}[/]", maxValue: items.Count);

                foreach (var item in items)
                {
                    try
                    {
                        processItem(item, task);
                    }
                    catch (Exception ex)
                    {
                        AddError(item?.ToString() ?? "unknown", ex.Message, ex);
                        if (Verbose)
                            LogError($"Error processing {item}: {ex.Message}");
                    }

                    task.Increment(1);
                }
            });
    }

    /// <summary>
    /// Run extraction with a status spinner for unknown duration.
    /// In quiet mode, just runs the action without spinner.
    /// </summary>
    protected T WithStatus<T>(string status, Func<StatusContext, T> action)
    {
        if (QuietMode)
            return action(null!);

        return AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .Start(status, action);
    }

    /// <summary>
    /// Run extraction with a status spinner (no return value).
    /// In quiet mode, just runs the action without spinner.
    /// </summary>
    protected void WithStatus(string status, Action<StatusContext> action)
    {
        if (QuietMode)
        {
            action(null!);
            return;
        }

        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .Start(status, action);
    }

    #endregion

    #region Logging Helpers

    /// <summary>
    /// Log an informational message (only in verbose mode and not quiet).
    /// </summary>
    protected void LogVerbose(string message)
    {
        if (Verbose && !QuietMode)
            AnsiConsole.MarkupLine($"[dim]  {Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Log a warning message.
    /// </summary>
    protected void LogWarning(string message)
    {
        _warnings.Add(message);
        if (Verbose && !QuietMode)
            AnsiConsole.MarkupLine($"[yellow]:warning: {Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Log an error message.
    /// </summary>
    protected void LogError(string message)
    {
        if (!QuietMode)
            AnsiConsole.MarkupLine($"[red]:cross_mark: {Markup.Escape(message)}[/]");
    }

    /// <summary>
    /// Record an error for the result.
    /// </summary>
    protected void AddError(string itemName, string message, Exception? ex = null)
    {
        _errors.Add(new ExtractionError(itemName, message, ex));
    }

    /// <summary>
    /// Record an additional count category.
    /// </summary>
    protected void SetCount(string category, int count)
    {
        _counts[category] = count;
    }

    /// <summary>
    /// Increment an additional count category.
    /// </summary>
    protected void IncrementCount(string category, int amount = 1)
    {
        _counts.TryGetValue(category, out var current);
        _counts[category] = current + amount;
    }

    #endregion

    #region Summary Display

    /// <summary>
    /// Display the extraction summary using Spectre Console table.
    /// </summary>
    protected virtual void DisplaySummary(ExtractionResult result)
    {
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(result.Success ? Color.Green : Color.Red)
            .Title($"[bold]{Markup.Escape(Name)}[/]")
            .AddColumn("Metric")
            .AddColumn(new TableColumn("Value").RightAligned());

        // Primary count
        table.AddRow("Items", $"[cyan]{result.ItemCount}[/]");

        // Additional counts
        foreach (var (key, value) in result.AdditionalCounts.OrderBy(x => x.Key))
        {
            table.AddRow(key, $"[cyan]{value}[/]");
        }

        table.AddEmptyRow();
        table.AddRow("[dim]Duration[/]", $"[dim]{result.Duration.TotalSeconds:F1}s[/]");

        if (result.HasErrors)
            table.AddRow("[red]Errors[/]", $"[red]{result.Errors.Count}[/]");

        if (result.HasWarnings)
            table.AddRow("[yellow]Warnings[/]", $"[yellow]{result.Warnings.Count}[/]");

        AnsiConsole.Write(table);

        // Status message
        if (result.Success)
            AnsiConsole.MarkupLine($"\n[green]:check_mark: {Markup.Escape(Name)} complete[/]");
        else
            AnsiConsole.MarkupLine($"\n[red]:cross_mark: {Markup.Escape(Name)} completed with errors[/]");

        // Show errors if verbose
        if (Verbose && result.HasErrors)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Errors:[/]");
            foreach (var error in result.Errors.Take(10))
            {
                var item = string.IsNullOrEmpty(error.ItemName) ? "" : $"[yellow]{Markup.Escape(error.ItemName)}[/]: ";
                AnsiConsole.MarkupLine($"  [grey]-[/] {item}{Markup.Escape(error.Message)}");
            }

            if (result.Errors.Count > 10)
                AnsiConsole.MarkupLine($"  [grey]... and {result.Errors.Count - 10} more[/]");
        }
    }

    #endregion

    #region Path Helpers

    /// <summary>
    /// Ensure a directory exists, creating it if necessary.
    /// </summary>
    protected static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    /// <summary>
    /// Get output path for graphics files.
    /// </summary>
    protected string GetGraphicsPath(params string[] subPaths)
    {
        var path = Path.Combine(new[] { OutputPath, "Graphics" }.Concat(subPaths).ToArray());
        EnsureDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>
    /// Get output path for definition JSON files.
    /// </summary>
    protected string GetDefinitionPath(params string[] subPaths)
    {
        var path = Path.Combine(new[] { OutputPath, "Definitions", "Assets" }.Concat(subPaths).ToArray());
        EnsureDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>
    /// Get output path for entity definition JSON files.
    /// </summary>
    protected string GetEntityPath(params string[] subPaths)
    {
        var path = Path.Combine(new[] { OutputPath, "Definitions", "Entities" }.Concat(subPaths).ToArray());
        EnsureDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    #endregion
}
