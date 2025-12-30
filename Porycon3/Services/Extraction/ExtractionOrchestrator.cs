using Spectre.Console;

namespace Porycon3.Services.Extraction;

/// <summary>
/// Orchestrates multiple extractors with a unified live display.
/// Shows all extractors in a single dynamic table that updates in real-time.
/// </summary>
public class ExtractionOrchestrator
{
    private readonly List<(IExtractor Extractor, string DisplayName)> _extractors = new();
    private readonly Dictionary<string, ExtractorStatus> _statuses = new();
    private readonly bool _verbose;

    public ExtractionOrchestrator(bool verbose = false)
    {
        _verbose = verbose;
    }

    /// <summary>
    /// Add an extractor to be orchestrated.
    /// </summary>
    public ExtractionOrchestrator Add(IExtractor extractor, string? displayName = null)
    {
        var name = displayName ?? extractor.Name;
        _extractors.Add((extractor, name));
        _statuses[name] = new ExtractorStatus { State = ExtractorState.Pending };
        return this;
    }

    /// <summary>
    /// Run all extractors with a unified live display.
    /// </summary>
    public Dictionary<string, ExtractionResult> RunAll()
    {
        var results = new Dictionary<string, ExtractionResult>();

        AnsiConsole.Live(BuildTable())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Start(ctx =>
            {
                foreach (var (extractor, name) in _extractors)
                {
                    // Update status to running
                    _statuses[name].State = ExtractorState.Running;
                    _statuses[name].StartTime = DateTime.Now;
                    ctx.Refresh();

                    try
                    {
                        // Run the extractor (it will handle its own progress internally)
                        var result = RunExtractorQuietly(extractor);
                        results[name] = result;

                        // Update status to complete
                        _statuses[name].State = result.Success ? ExtractorState.Complete : ExtractorState.Failed;
                        _statuses[name].Result = result;
                        _statuses[name].EndTime = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        _statuses[name].State = ExtractorState.Failed;
                        _statuses[name].Error = ex.Message;
                        _statuses[name].EndTime = DateTime.Now;
                    }

                    ctx.UpdateTarget(BuildTable());
                }
            });

        // Print final summary
        PrintSummary(results);

        return results;
    }

    /// <summary>
    /// Run extractor without its own display (suppress Spectre output).
    /// </summary>
    private static ExtractionResult RunExtractorQuietly(IExtractor extractor)
    {
        extractor.QuietMode = true;
        return extractor.ExtractAll();
    }

    private Table BuildTable()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .Title("[bold cyan]Asset Extraction[/]")
            .AddColumn(new TableColumn("[bold]Extractor[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Status[/]").Centered())
            .AddColumn(new TableColumn("[bold]Items[/]").RightAligned())
            .AddColumn(new TableColumn("[bold]Time[/]").RightAligned());

        foreach (var (_, name) in _extractors)
        {
            var status = _statuses[name];
            var (statusIcon, statusColor) = status.State switch
            {
                ExtractorState.Pending => ("○", "grey"),
                ExtractorState.Running => ("◐", "yellow"),
                ExtractorState.Complete => ("✓", "green"),
                ExtractorState.Failed => ("✗", "red"),
                _ => ("?", "white")
            };

            var itemCount = status.Result?.ItemCount.ToString() ?? "-";
            var duration = status.EndTime.HasValue && status.StartTime.HasValue
                ? $"{(status.EndTime.Value - status.StartTime.Value).TotalSeconds:F1}s"
                : status.StartTime.HasValue
                    ? "..."
                    : "-";

            table.AddRow(
                $"[white]{Markup.Escape(name)}[/]",
                $"[{statusColor}]{statusIcon}[/]",
                $"[cyan]{itemCount}[/]",
                $"[dim]{duration}[/]"
            );
        }

        return table;
    }

    private void PrintSummary(Dictionary<string, ExtractionResult> results)
    {
        AnsiConsole.WriteLine();

        var totalItems = results.Values.Sum(r => r.ItemCount);
        var totalDuration = results.Values.Sum(r => r.Duration.TotalSeconds);
        var successCount = results.Values.Count(r => r.Success);
        var failCount = results.Values.Count(r => !r.Success);

        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(failCount > 0 ? Color.Yellow : Color.Green)
            .Title("[bold]Extraction Summary[/]")
            .AddColumn("Metric")
            .AddColumn(new TableColumn("Value").RightAligned());

        summaryTable.AddRow("Total Items", $"[cyan]{totalItems:N0}[/]");
        summaryTable.AddRow("Total Duration", $"[dim]{totalDuration:F1}s[/]");
        summaryTable.AddRow("Extractors", $"[green]{successCount} passed[/]" + (failCount > 0 ? $", [red]{failCount} failed[/]" : ""));

        // Add detailed counts for each extractor with additional counts
        summaryTable.AddEmptyRow();
        foreach (var (name, result) in results.OrderBy(r => r.Key))
        {
            if (result.AdditionalCounts.Count > 0)
            {
                var details = string.Join(", ", result.AdditionalCounts.Select(c => $"{c.Key}: {c.Value}"));
                summaryTable.AddRow($"[dim]{Markup.Escape(name)}[/]", $"[dim]{result.ItemCount} ({details})[/]");
            }
        }

        AnsiConsole.Write(summaryTable);

        // Show errors if any
        if (_verbose)
        {
            var allErrors = results.Values.SelectMany(r => r.Errors).ToList();
            if (allErrors.Any())
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[red]Errors:[/]");
                foreach (var error in allErrors.Take(10))
                {
                    AnsiConsole.MarkupLine($"  [grey]-[/] {Markup.Escape(error.ItemName)}: {Markup.Escape(error.Message)}");
                }
                if (allErrors.Count > 10)
                    AnsiConsole.MarkupLine($"  [grey]... and {allErrors.Count - 10} more[/]");
            }
        }
    }

    private class ExtractorStatus
    {
        public ExtractorState State { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public ExtractionResult? Result { get; set; }
        public string? Error { get; set; }
    }

    private enum ExtractorState
    {
        Pending,
        Running,
        Complete,
        Failed
    }
}
