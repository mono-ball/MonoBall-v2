using Spectre.Console;
using Spectre.Console.Rendering;

namespace Porycon3.Services.Progress;

/// <summary>
/// Builds immutable Spectre Console renderables from progress snapshots.
/// All methods return new objects - safe for Live context rendering.
/// </summary>
public static class ProgressRenderer
{
    /// <summary>
    /// Build the complete display layout for the current progress state.
    /// </summary>
    public static IRenderable BuildDisplay(ProgressSnapshot snapshot)
    {
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Progress").Size(6),
                new Layout("Details"),
                new Layout("Status").Size(1));

        layout["Header"].Update(BuildHeader(snapshot));
        layout["Progress"].Update(BuildProgressTable(snapshot));
        layout["Details"].Update(BuildDetails(snapshot));
        layout["Status"].Update(BuildStatusBar(snapshot));

        return layout;
    }

    /// <summary>
    /// Build a simpler display without layout (for smaller terminals).
    /// </summary>
    public static IRenderable BuildSimpleDisplay(ProgressSnapshot snapshot)
    {
        var rows = new Rows(
            BuildHeader(snapshot),
            new Rule().RuleStyle("dim"),
            BuildProgressTable(snapshot),
            new Rule().RuleStyle("dim"),
            BuildDetails(snapshot),
            BuildStatusBar(snapshot));

        return rows;
    }

    private static IRenderable BuildHeader(ProgressSnapshot snapshot)
    {
        var phase = snapshot.Phase switch
        {
            ConversionPhase.Initializing => "[yellow]Initializing[/]",
            ConversionPhase.ScanningMaps => "[cyan]Scanning Maps[/]",
            ConversionPhase.ConvertingMaps => "[blue]Converting Maps[/]",
            ConversionPhase.FinalizingTilesets => "[magenta]Finalizing Tilesets[/]",
            ConversionPhase.ExtractingAssets => "[green]Extracting Assets[/]",
            ConversionPhase.Complete => "[green bold]Complete[/]",
            ConversionPhase.Failed => "[red bold]Failed[/]",
            _ => "[grey]Unknown[/]"
        };

        return new Markup($"[bold cyan]Porycon3[/] │ {phase} │ [dim]{snapshot.TotalElapsed:mm\\:ss}[/]");
    }

    private static IRenderable BuildProgressTable(ProgressSnapshot snapshot)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Expand();

        table.AddColumn(new TableColumn("Phase").Width(20));
        table.AddColumn(new TableColumn("Progress").Width(30));
        table.AddColumn(new TableColumn("Status").Width(15));

        // Maps row
        var mapProgress = snapshot.MapTotal > 0
            ? $"{snapshot.MapCompleted}/{snapshot.MapTotal}"
            : "-";
        var mapBar = snapshot.MapTotal > 0
            ? BuildProgressBar(snapshot.MapCompleted, snapshot.MapTotal)
            : "[dim]waiting[/]";
        var mapStatus = snapshot.Phase == ConversionPhase.ConvertingMaps
            ? "[blue]●[/] Active"
            : snapshot.MapCompleted > 0 ? "[green]✓[/] Done" : "[dim]○[/] Pending";

        if (snapshot.MapFailed > 0)
            mapStatus = $"[yellow]⚠[/] {snapshot.MapFailed} failed";

        table.AddRow("Maps", mapBar, mapStatus);

        // Tilesets row
        var tilesetBar = snapshot.TilesetTotal > 0
            ? BuildProgressBar(snapshot.TilesetCompleted, snapshot.TilesetTotal)
            : "[dim]waiting[/]";
        var tilesetStatus = snapshot.Phase == ConversionPhase.FinalizingTilesets
            ? "[blue]●[/] Active"
            : snapshot.TilesetCompleted > 0 ? "[green]✓[/] Done" : "[dim]○[/] Pending";

        table.AddRow("Tilesets", tilesetBar, tilesetStatus);

        // Extractors summary row
        var extractorsDone = snapshot.Extractors.Count(e => e.State == ExtractorState.Complete);
        var extractorsTotal = snapshot.Extractors.Length;
        var extractorBar = extractorsTotal > 0
            ? BuildProgressBar(extractorsDone, extractorsTotal)
            : "[dim]waiting[/]";
        var extractorStatus = snapshot.Phase == ConversionPhase.ExtractingAssets
            ? "[blue]●[/] Active"
            : extractorsDone > 0 && extractorsDone == extractorsTotal ? "[green]✓[/] Done" : "[dim]○[/] Pending";

        table.AddRow("Extractors", extractorBar, extractorStatus);

        return table;
    }

    private static IRenderable BuildDetails(ProgressSnapshot snapshot)
    {
        return snapshot.Phase switch
        {
            ConversionPhase.Initializing => new Markup("[dim]Starting up...[/]"),
            ConversionPhase.ScanningMaps => new Markup("[cyan]Scanning for maps in data/maps/...[/]"),
            ConversionPhase.ConvertingMaps => BuildMapDetails(snapshot),
            ConversionPhase.FinalizingTilesets => new Markup("[magenta]Building shared tilesheets...[/]"),
            ConversionPhase.ExtractingAssets => BuildExtractorDetails(snapshot),
            ConversionPhase.Complete => BuildCompletionDetails(snapshot),
            ConversionPhase.Failed => new Markup("[red]Conversion failed. Check logs for details.[/]"),
            _ => new Markup("[dim]Working...[/]")
        };
    }

    private static IRenderable BuildCompletionDetails(ProgressSnapshot snapshot)
    {
        var components = new List<IRenderable>();

        // Show the extractor details table
        components.Add(BuildExtractorDetails(snapshot));

        // Add summary stats
        var extractorsDone = snapshot.Extractors.Count(e => e.State == ExtractorState.Complete);
        var extractorsFailed = snapshot.Extractors.Count(e => e.State == ExtractorState.Failed);
        var totalItems = snapshot.Extractors.Sum(e => e.ItemCount);

        var summaryParts = new List<string>
        {
            $"[green]{snapshot.MapCompleted}[/] maps",
            $"[green]{snapshot.TilesetCompleted}[/] tilesets",
            $"[green]{totalItems:N0}[/] assets"
        };

        // Add warnings/errors if any
        if (snapshot.MapFailed > 0 || extractorsFailed > 0)
        {
            components.Add(new Rule().RuleStyle("dim"));

            var warningLines = new List<IRenderable>();

            if (snapshot.MapFailed > 0)
            {
                warningLines.Add(new Markup($"[yellow]⚠[/] [yellow]{snapshot.MapFailed}[/] map(s) failed to convert"));
            }

            if (extractorsFailed > 0)
            {
                var failedExtractors = snapshot.Extractors
                    .Where(e => e.State == ExtractorState.Failed)
                    .Select(e => e.Name);
                warningLines.Add(new Markup($"[red]✗[/] [red]{extractorsFailed}[/] extractor(s) failed: {string.Join(", ", failedExtractors)}"));
            }

            components.Add(new Rows(warningLines));
        }

        // Final summary line
        components.Add(new Rule().RuleStyle("dim"));
        components.Add(new Markup($"[green bold]✓ Complete[/] │ {string.Join(" │ ", summaryParts)} │ [dim]{snapshot.TotalElapsed:mm\\:ss\\.fff}[/]"));

        return new Rows(components);
    }

    private static IRenderable BuildMapDetails(ProgressSnapshot snapshot)
    {
        if (snapshot.RecentMaps.Length == 0)
            return new Markup("[dim]Starting map conversion...[/]");

        var panel = new Panel(
            new Rows(snapshot.RecentMaps.TakeLast(5).Select(m => new Markup($"[dim]•[/] {m}"))))
            .Header("[dim]Recent Maps[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey);

        return panel;
    }

    private static IRenderable BuildExtractorDetails(ProgressSnapshot snapshot)
    {
        if (snapshot.Extractors.Length == 0)
            return new Markup("[dim]Starting extraction...[/]");

        var table = new Table()
            .Border(TableBorder.Simple)
            .BorderColor(Color.Grey)
            .Expand();

        table.AddColumn(new TableColumn("Extractor").Width(25));
        table.AddColumn(new TableColumn("Items").Width(10).RightAligned());
        table.AddColumn(new TableColumn("Time").Width(10).RightAligned());
        table.AddColumn(new TableColumn("Status").Width(12));

        foreach (var extractor in snapshot.Extractors)
        {
            var (icon, statusText) = extractor.State switch
            {
                ExtractorState.Pending => ("[dim]○[/]", "[dim]Pending[/]"),
                ExtractorState.Running => ("[blue]●[/]", "[blue]Running[/]"),
                ExtractorState.Complete => ("[green]✓[/]", "[green]Done[/]"),
                ExtractorState.Failed => ("[red]✗[/]", "[red]Failed[/]"),
                ExtractorState.Skipped => ("[yellow]–[/]", "[yellow]Skipped[/]"),
                _ => ("[grey]?[/]", "[grey]Unknown[/]")
            };

            var items = extractor.ItemCount > 0 ? extractor.ItemCount.ToString() : "-";
            var time = extractor.Elapsed.TotalMilliseconds > 0
                ? $"{extractor.Elapsed.TotalMilliseconds:F0}ms"
                : "-";

            table.AddRow(
                $"{icon} {extractor.Name}",
                items,
                time,
                statusText);
        }

        return table;
    }

    private static IRenderable BuildCompleteSummary(ProgressSnapshot snapshot)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow("[green]Maps converted:[/]", $"{snapshot.MapCompleted}");
        if (snapshot.MapFailed > 0)
            grid.AddRow("[yellow]Maps failed:[/]", $"{snapshot.MapFailed}");
        grid.AddRow("[green]Tilesets generated:[/]", $"{snapshot.TilesetCompleted}");

        var extractorsDone = snapshot.Extractors.Count(e => e.State == ExtractorState.Complete);
        grid.AddRow("[green]Extractors completed:[/]", $"{extractorsDone}/{snapshot.Extractors.Length}");
        grid.AddRow("[dim]Total time:[/]", $"{snapshot.TotalElapsed:mm\\:ss\\.fff}");

        return new Panel(grid)
            .Header("[green bold]Conversion Complete[/]")
            .Border(BoxBorder.Double)
            .BorderColor(Color.Green);
    }

    private static IRenderable BuildStatusBar(ProgressSnapshot snapshot)
    {
        var activeExtractor = snapshot.Extractors
            .FirstOrDefault(e => e.State == ExtractorState.Running);

        var statusText = activeExtractor != null
            ? $"[dim]Extracting:[/] {activeExtractor.Name}"
            : snapshot.Phase switch
            {
                ConversionPhase.ScanningMaps => "[dim]Discovering maps...[/]",
                ConversionPhase.ConvertingMaps => $"[dim]Converting map {snapshot.MapCompleted + 1} of {snapshot.MapTotal}[/]",
                ConversionPhase.FinalizingTilesets => "[dim]Generating shared tilesets...[/]",
                ConversionPhase.Complete => "", // Summary panel shows completion
                _ => "[dim]Working...[/]"
            };

        return new Markup(statusText);
    }

    private static string BuildProgressBar(int current, int total)
    {
        if (total <= 0) return "[dim]waiting[/]";

        var percent = (double)current / total;
        var filled = (int)(percent * 20);
        var empty = 20 - filled;

        var bar = new string('█', filled) + new string('░', empty);
        var percentText = $"{percent * 100:F0}%";

        return $"[green]{bar}[/] {percentText}";
    }
}
