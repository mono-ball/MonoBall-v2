using System;
using System.IO;
using Spectre.Console;

namespace MonoBall.ArchiveTool;

/// <summary>
///     Command-line tool for creating and managing .monoball mod archives.
/// </summary>
public class Program
{
    private static bool IsNonInteractive { get; set; }

    private static int Main(string[] args)
    {
        // Check if running in non-interactive mode (e.g., from MSBuild)
        IsNonInteractive = !AnsiConsole.Profile.Capabilities.Interactive ||
                           Environment.GetEnvironmentVariable("NO_COLOR") != null ||
                           Environment.GetEnvironmentVariable("CI") != null;

        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "pack" => PackCommand(args),
                "unpack" => UnpackCommand(args),
                "info" => InfoCommand(args),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            if (IsNonInteractive)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null) Console.Error.WriteLine($"  {ex.InnerException.Message}");
            }
            else
            {
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes);
            }

            return 1;
        }
    }

    private static int PackCommand(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] 'pack' command requires a mod directory path.");
            AnsiConsole.MarkupLine(
                "[dim]Usage: MonoBall.ArchiveTool pack <mod-directory> [--output <path>] [--compression-level <1-9>][/]");
            return 1;
        }

        var modDirectory = args[1];
        string? outputPath = null;
        var compressionLevel = 1;

        // Parse optional arguments
        for (var i = 2; i < args.Length; i++)
            if (args[i] == "--output" && i + 1 < args.Length)
                outputPath = args[++i];
            else if (args[i] == "--compression-level" && i + 1 < args.Length)
                if (!int.TryParse(args[++i], out compressionLevel) || compressionLevel < 1 || compressionLevel > 9)
                {
                    AnsiConsole.MarkupLine("[red]Error:[/] Compression level must be between 1 and 9.");
                    return 1;
                }

        // Validate mod directory exists
        if (!Directory.Exists(modDirectory))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Mod directory not found: [yellow]{modDirectory}[/]");
            return 1;
        }

        // Default output path: mod-directory.monoball in same directory as mod
        if (outputPath == null)
        {
            var modDirInfo = new DirectoryInfo(modDirectory);
            var parentDir = modDirInfo.Parent?.FullName ?? Path.GetDirectoryName(modDirectory) ?? ".";
            outputPath = Path.Combine(parentDir, $"{modDirInfo.Name}.monoball");
        }

        // Count files
        var files = Directory.GetFiles(modDirectory, "*", SearchOption.AllDirectories);
        var totalSize = GetDirectorySize(modDirectory);

        if (!IsNonInteractive)
        {
            // Display header
            AnsiConsole.Write(new Rule("[bold blue]MonoBall Archive Tool[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var grid = new Grid();
            grid.AddColumn();
            grid.AddColumn();
            grid.AddRow("[bold]Source:[/]", $"[cyan]{modDirectory}[/]");
            grid.AddRow("[bold]Output:[/]", $"[cyan]{outputPath}[/]");
            grid.AddRow("[bold]Compression:[/]", $"[green]LZ4 Level {compressionLevel}[/]");
            AnsiConsole.Write(grid);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine($"[dim]Found [bold]{files.Length}[/] files ({FormatBytes(totalSize)})[/]");
            AnsiConsole.WriteLine();

            // Create archive with progress bar
            var creator = new ArchiveCreator();

            AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                )
                .Start(progressCtx =>
                {
                    var task = progressCtx.AddTask("[green]Compressing files[/]", maxValue: files.Length);
                    var bytesTask = progressCtx.AddTask("[cyan]Processing data[/]", maxValue: totalSize);

                    creator.ProgressCallback = (current, total, filePath, bytesProcessed, totalBytes) =>
                    {
                        task.Value = current;
                        task.Description = $"[green]Compressing[/] [dim]{Path.GetFileName(filePath)}[/]";
                        bytesTask.Value = bytesProcessed;
                    };

                    creator.CreateArchive(modDirectory, outputPath, compressionLevel);
                });
        }
        else
        {
            // Non-interactive mode: simple console output
            Console.WriteLine($"Packing mod from '{modDirectory}' to '{outputPath}'...");
            Console.WriteLine($"Compression level: {compressionLevel}");
            Console.WriteLine($"Found {files.Length} files ({FormatBytes(totalSize)})");

            var creator = new ArchiveCreator();
            creator.ProgressCallback = (current, total, filePath, bytesProcessed, totalBytes) =>
            {
                if (current % 10 == 0 || current == total)
                {
                    var percent = total > 0 ? current * 100 / total : 0;
                    Console.WriteLine($"Progress: {current}/{total} files ({percent}%)");
                }
            };

            creator.CreateArchive(modDirectory, outputPath, compressionLevel);
        }

        // Display results
        var outputSize = new FileInfo(outputPath).Length;
        var compressionRatio = totalSize > 0 ? (1.0 - (double)outputSize / totalSize) * 100.0 : 0.0;
        var spaceSaved = totalSize - outputSize;

        if (!IsNonInteractive)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold green]Archive Created Successfully[/]").LeftJustified());
            AnsiConsole.WriteLine();

            var resultTable = new Table().Border(TableBorder.Rounded);
            resultTable.AddColumn("[bold]Metric[/]");
            resultTable.AddColumn("[bold]Value[/]");
            resultTable.AddRow(
                "[cyan]Files[/]",
                $"[bold]{files.Length}[/]"
            );
            resultTable.AddRow(
                "[cyan]Input Size[/]",
                $"[bold]{FormatBytes(totalSize)}[/]"
            );
            resultTable.AddRow(
                "[cyan]Output Size[/]",
                $"[bold]{FormatBytes(outputSize)}[/]"
            );
            resultTable.AddRow(
                "[cyan]Space Saved[/]",
                $"[bold green]{FormatBytes(spaceSaved)}[/]"
            );
            resultTable.AddRow(
                "[cyan]Compression Ratio[/]",
                $"[bold green]{compressionRatio:F1}%[/]"
            );

            AnsiConsole.Write(resultTable);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✓[/] Archive saved to: [bold cyan]{outputPath}[/]");
        }
        else
        {
            Console.WriteLine("Archive created successfully!");
            Console.WriteLine($"  Files: {files.Length}");
            Console.WriteLine($"  Input size: {FormatBytes(totalSize)}");
            Console.WriteLine($"  Output size: {FormatBytes(outputSize)}");
            Console.WriteLine($"  Space saved: {FormatBytes(spaceSaved)}");
            Console.WriteLine($"  Compression ratio: {compressionRatio:F1}%");
        }

        return 0;
    }

    private static int UnpackCommand(string[] args)
    {
        AnsiConsole.MarkupLine("[yellow]⚠[/] [bold]'unpack' command is not yet implemented.[/]");
        AnsiConsole.MarkupLine("[dim]This feature will be available in a future release.[/]");
        return 1;
    }

    private static int InfoCommand(string[] args)
    {
        if (args.Length < 2)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] 'info' command requires an archive file path.");
            AnsiConsole.MarkupLine("[dim]Usage: MonoBall.ArchiveTool info <archive-file>[/]");
            return 1;
        }

        var archivePath = args[1];

        if (!File.Exists(archivePath))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Archive file not found: [yellow]{archivePath}[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[yellow]⚠[/] [bold]'info' command is not yet implemented.[/]");
        AnsiConsole.MarkupLine("[dim]This feature will be available in a future release.[/]");
        return 1;
    }

    private static int UnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] Unknown command '[yellow]{command}[/]'.");
        AnsiConsole.WriteLine();
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        AnsiConsole.Write(new FigletText("MonoBall").Color(Color.Blue));
        AnsiConsole.Write(new FigletText("Archive Tool").Color(new Color(0, 255, 255))); // Cyan RGB
        AnsiConsole.WriteLine();

        var usageTable = new Table().Border(TableBorder.Rounded);
        usageTable.AddColumn("[bold]Command[/]");
        usageTable.AddColumn("[bold]Description[/]");
        usageTable.AddRow(
            "[cyan]pack[/]",
            "Create a .monoball archive from a mod directory"
        );
        usageTable.AddRow(
            "[yellow]unpack[/]",
            "Extract a .monoball archive to a directory [dim](not implemented)[/]"
        );
        usageTable.AddRow(
            "[yellow]info[/]",
            "Display information about a .monoball archive [dim](not implemented)[/]"
        );
        AnsiConsole.Write(usageTable);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine("[bold]Examples:[/]");
        AnsiConsole.MarkupLine("[dim]  MonoBall.ArchiveTool pack Mods/core[/]");
        AnsiConsole.MarkupLine("[dim]  MonoBall.ArchiveTool pack Mods/core --output Mods/core.monoball[/]");
        AnsiConsole.MarkupLine("[dim]  MonoBall.ArchiveTool pack Mods/core --compression-level 3[/]");
        AnsiConsole.WriteLine();

        var optionsTable = new Table().Border(TableBorder.Rounded);
        optionsTable.AddColumn("[bold]Option[/]");
        optionsTable.AddColumn("[bold]Description[/]");
        optionsTable.AddRow(
            "[cyan]--output <path>[/]",
            "Output path for archive (default: <mod-name>.monoball)"
        );
        optionsTable.AddRow(
            "[cyan]--compression-level <1-9>[/]",
            "LZ4 compression level (default: 1, fastest)"
        );
        AnsiConsole.Write(optionsTable);
    }

    private static long GetDirectorySize(string directory)
    {
        long size = 0;
        var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        foreach (var file in files)
            try
            {
                size += new FileInfo(file).Length;
            }
            catch
            {
                // Ignore files we can't access
            }

        return size;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }
}