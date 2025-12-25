# Spectre.Console Best Practices for C# CLI Applications
## Research Summary for Pokemon ROM Hacking Tool

---

## Table of Contents
1. [Command Registration Patterns](#1-command-registration-patterns)
2. [Dependency Injection Integration](#2-dependency-injection-integration)
3. [Configuration Management](#3-configuration-management)
4. [Progress Indicators and Status Displays](#4-progress-indicators-and-status-displays)
5. [Table and Tree Rendering](#5-table-and-tree-rendering)
6. [Error Handling Patterns](#6-error-handling-patterns)
7. [Testing Strategies](#7-testing-strategies)
8. [Project Structure Recommendations](#8-project-structure-recommendations)
9. [Pokemon ROM Tool Specific Patterns](#9-pokemon-rom-tool-specific-patterns)

---

## 1. Command Registration Patterns

### Basic Command Structure

Commands inherit from `Command<TSettings>` or `AsyncCommand<TSettings>`:

```csharp
// Synchronous command
public class ParseMapCommand : Command<ParseMapSettings>
{
    public override int Execute(CommandContext context, ParseMapSettings settings)
    {
        // Command logic
        return 0;
    }
}

// Asynchronous command (recommended for I/O operations)
public class ExportDataCommand : AsyncCommand<ExportDataSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ExportDataSettings settings)
    {
        // Async command logic
        return 0;
    }
}
```

### Command Registration and Configuration

```csharp
// Program.cs
var app = new CommandApp();
app.Configure(config =>
{
    // Simple command registration
    config.AddCommand<ParseMapCommand>("parse-map")
        .WithDescription("Parse map data from ROM")
        .WithAlias("pm")
        .WithExample(new[] { "parse-map", "--input", "map_01.bin" });

    // Hierarchical command structure using branches
    config.AddBranch("data", data =>
    {
        data.SetDescription("Data manipulation commands");

        data.AddCommand<ExportDataCommand>("export")
            .WithDescription("Export data to JSON/CSV")
            .WithExample(new[] { "data", "export", "--format", "json" });

        data.AddCommand<ImportDataCommand>("import")
            .WithDescription("Import data from external sources");
    });

    // Pokemon-specific command structure
    config.AddBranch("pokemon", pokemon =>
    {
        pokemon.AddCommand<ListPokemonCommand>("list");
        pokemon.AddCommand<EditPokemonCommand>("edit");
        pokemon.AddCommand<AddPokemonCommand>("add");
    });
});

return app.Run(args);
```

### Command Settings with Validation

```csharp
public class ParseMapSettings : CommandSettings
{
    [CommandArgument(0, "<INPUT_FILE>")]
    [Description("Path to the input ROM file")]
    public string InputFile { get; init; }

    [CommandOption("-o|--output")]
    [Description("Output directory for parsed data")]
    [DefaultValue("./output")]
    public string OutputDir { get; init; }

    [CommandOption("-f|--format")]
    [Description("Output format (json, csv, binary)")]
    [DefaultValue("json")]
    public string Format { get; init; }

    // Custom validation
    public override ValidationResult Validate()
    {
        if (!File.Exists(InputFile))
        {
            return ValidationResult.Error($"Input file '{InputFile}' does not exist");
        }

        if (!new[] { "json", "csv", "binary" }.Contains(Format.ToLower()))
        {
            return ValidationResult.Error("Format must be json, csv, or binary");
        }

        return ValidationResult.Success();
    }
}
```

### Interceptors for Global Operations

```csharp
public class LoggingInterceptor : ICommandInterceptor
{
    private readonly ILogger _logger;

    public LoggingInterceptor(ILogger logger)
    {
        _logger = logger;
    }

    public void Intercept(CommandContext context, CommandSettings settings)
    {
        _logger.LogInformation($"Executing command: {context.Name}");

        // Setup global state, logging, performance tracking, etc.
        _logger.LogDebug($"Command settings: {JsonSerializer.Serialize(settings)}");
    }
}

// In configuration:
config.SetInterceptor(new LoggingInterceptor(logger));
```

---

## 2. Dependency Injection Integration

### Available NuGet Packages

There are three main packages for DI integration:

1. **Spectre.Console.Cli.Extensions.DependencyInjection** (v0.18.0+)
2. **D20Tek.Spectre.Console.Extensions** (v1.53.1+) - Recommended for comprehensive features
3. **Spectre.Console.Registrars.Microsoft-DI**

### Basic Setup with Microsoft.Extensions.DependencyInjection

```csharp
// Install packages:
// - Spectre.Console.Cli
// - Microsoft.Extensions.DependencyInjection
// - Spectre.Console.Cli.Extensions.DependencyInjection

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

// Program.cs
var services = new ServiceCollection();

// Register application services
services.AddSingleton<IRomParser, GbaRomParser>();
services.AddSingleton<IDataExporter, JsonDataExporter>();
services.AddSingleton<IPokemonRepository, PokemonRepository>();
services.AddSingleton<ILogger, ConsoleLogger>();

// Optional: Add configuration
services.AddSingleton(new AppConfiguration
{
    RomPath = "./pokemon.gba",
    CacheEnabled = true
});

// Create registrar and CommandApp
using var registrar = new DependencyInjectionRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.AddCommand<ParseMapCommand>("parse-map");
    // ... other commands
});

return app.Run(args);
```

### Command with Dependency Injection

```csharp
public class ParseMapCommand : AsyncCommand<ParseMapSettings>
{
    private readonly IRomParser _romParser;
    private readonly IDataExporter _exporter;
    private readonly ILogger _logger;

    // Constructor injection - services automatically resolved
    public ParseMapCommand(
        IRomParser romParser,
        IDataExporter exporter,
        ILogger logger)
    {
        _romParser = romParser;
        _exporter = exporter;
        _logger = logger;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ParseMapSettings settings)
    {
        _logger.LogInfo($"Parsing map from: {settings.InputFile}");

        var mapData = await _romParser.ParseMapAsync(settings.InputFile);
        await _exporter.ExportAsync(mapData, settings.OutputDir, settings.Format);

        return 0;
    }
}
```

### Advanced DI Setup with Scoped Services

```csharp
// For scenarios needing scoped lifetime (database contexts, etc.)
var services = new ServiceCollection();

services.AddSingleton<IRomFileSystem, RomFileSystem>();
services.AddScoped<IMapParser, MapParser>();
services.AddScoped<IPokemonDataContext, PokemonDataContext>();

// Transient services created per request
services.AddTransient<IValidationService, ValidationService>();
```

---

## 3. Configuration Management

### Configuration Options Pattern

```csharp
public class RomToolConfiguration
{
    public string DefaultRomPath { get; set; }
    public string CacheDirectory { get; set; }
    public bool EnableAutoBackup { get; set; }
    public LogLevel LogLevel { get; set; }
    public ExportSettings Export { get; set; }
}

public class ExportSettings
{
    public string DefaultFormat { get; set; }
    public bool PrettyPrint { get; set; }
    public int IndentSize { get; set; }
}

// Load from JSON configuration file
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ENV")}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var appConfig = configuration.Get<RomToolConfiguration>();
services.AddSingleton(appConfig);
```

### Per-Command Configuration

```csharp
app.Configure(config =>
{
    // Global configuration
    config.SetApplicationName("porycon3");
    config.SetApplicationVersion("3.0.0");
    config.ValidateExamples();

    // Case sensitivity
    config.CaseSensitivity(CaseSensitivity.None);

    // Custom exception handling
    config.SetExceptionHandler((ex, resolver) =>
    {
        AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
        return -1;
    });

    // Propagate exceptions for debugging
#if DEBUG
    config.PropagateExceptions();
#endif
});
```

---

## 4. Progress Indicators and Status Displays

### Progress Display for Long-Running Tasks

```csharp
public async Task<int> ExecuteAsync(CommandContext context, ParseRomSettings settings)
{
    await AnsiConsole.Progress()
        .AutoRefresh(true)
        .AutoClear(false)
        .HideCompleted(false)
        .Columns(new ProgressColumn[]
        {
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new RemainingTimeColumn(),
            new SpinnerColumn(),
        })
        .StartAsync(async ctx =>
        {
            // Parse Pokemon data
            var pokemonTask = ctx.AddTask("[green]Parsing Pokemon data[/]", maxValue: 151);
            for (int i = 0; i < 151; i++)
            {
                await ParsePokemonAsync(i);
                pokemonTask.Increment(1);
            }

            // Parse moves
            var movesTask = ctx.AddTask("[blue]Parsing moves[/]", maxValue: 165);
            for (int i = 0; i < 165; i++)
            {
                await ParseMoveAsync(i);
                movesTask.Increment(1);
            }

            // Parse maps
            var mapsTask = ctx.AddTask("[yellow]Parsing maps[/]", maxValue: 50);
            for (int i = 0; i < 50; i++)
            {
                await ParseMapAsync(i);
                mapsTask.Increment(1);
            }
        });

    return 0;
}
```

### Status Display for Indeterminate Operations

```csharp
public async Task<int> ExecuteAsync(CommandContext context, ExportSettings settings)
{
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("green bold"))
        .StartAsync("[yellow]Initializing export...[/]", async ctx =>
        {
            ctx.Status("[yellow]Loading ROM data...[/]");
            var romData = await LoadRomDataAsync();

            ctx.Status("[yellow]Analyzing data structures...[/]");
            var structures = AnalyzeStructures(romData);

            ctx.Status("[yellow]Exporting to JSON...[/]");
            await ExportToJsonAsync(structures, settings.OutputPath);

            ctx.Status("[green]Export complete![/]");
        });

    return 0;
}
```

### Combined Status and Progress (Workaround)

Since mixing Status and Progress isn't directly supported, use sequential patterns:

```csharp
public async Task<int> ExecuteAsync(CommandContext context, ProcessSettings settings)
{
    // Phase 1: Initialization with status
    await AnsiConsole.Status()
        .StartAsync("[yellow]Initializing...[/]", async ctx =>
        {
            ctx.Status("Loading ROM...");
            await InitializeRomAsync();

            ctx.Status("Validating data...");
            await ValidateDataAsync();
        });

    // Phase 2: Processing with progress
    await AnsiConsole.Progress()
        .StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Processing data[/]", maxValue: 100);
            for (int i = 0; i < 100; i++)
            {
                await ProcessItemAsync(i);
                task.Increment(1);
            }
        });

    // Phase 3: Cleanup with status
    await AnsiConsole.Status()
        .StartAsync("[yellow]Finalizing...[/]", async ctx =>
        {
            ctx.Status("Writing output files...");
            await WriteOutputAsync();

            ctx.Status("Cleaning up resources...");
            await CleanupAsync();
        });

    return 0;
}
```

### Live Display for Real-Time Updates

```csharp
var table = new Table();
table.AddColumn("Pokemon");
table.AddColumn("Progress");

await AnsiConsole.Live(table)
    .AutoClear(false)
    .StartAsync(async ctx =>
    {
        foreach (var pokemon in pokemonList)
        {
            table.AddRow(pokemon.Name, "[green]Processing...[/]");
            ctx.Refresh();

            await ProcessPokemonAsync(pokemon);

            table.UpdateCell(table.Rows.Count - 1, 1, "[green]✓ Complete[/]");
            ctx.Refresh();
        }
    });
```

---

## 5. Table and Tree Rendering

### Basic Table for Data Display

```csharp
public void DisplayPokemonList(IEnumerable<Pokemon> pokemon)
{
    var table = new Table();
    table.Border(TableBorder.Rounded);
    table.BorderColor(Color.Blue);

    // Add columns with styling
    table.AddColumn(new TableColumn("[yellow]ID[/]").Centered());
    table.AddColumn(new TableColumn("[yellow]Name[/]").LeftAligned());
    table.AddColumn(new TableColumn("[yellow]Type 1[/]").Centered());
    table.AddColumn(new TableColumn("[yellow]Type 2[/]").Centered());
    table.AddColumn(new TableColumn("[yellow]Base Stats[/]").RightAligned());

    foreach (var p in pokemon)
    {
        table.AddRow(
            p.Id.ToString(),
            $"[green]{Markup.Escape(p.Name)}[/]",
            GetTypeMarkup(p.Type1),
            p.Type2.HasValue ? GetTypeMarkup(p.Type2.Value) : "-",
            p.BaseStatTotal.ToString()
        );
    }

    AnsiConsole.Write(table);
}

private string GetTypeMarkup(PokemonType type)
{
    var color = type switch
    {
        PokemonType.Fire => "red",
        PokemonType.Water => "blue",
        PokemonType.Grass => "green",
        PokemonType.Electric => "yellow",
        _ => "white"
    };
    return $"[{color}]{type}[/]";
}
```

### Advanced Table with Nested Content

```csharp
public void DisplayPokemonDetails(Pokemon pokemon)
{
    var mainTable = new Table();
    mainTable.Border(TableBorder.Rounded);
    mainTable.Title($"[bold yellow]{pokemon.Name}[/]");

    // Basic info section
    var infoTable = new Table().BorderColor(Color.Grey);
    infoTable.AddColumn("Property");
    infoTable.AddColumn("Value");
    infoTable.HideHeaders();
    infoTable.AddRow("[blue]Type[/]", GetTypeMarkup(pokemon.Type1));
    infoTable.AddRow("[blue]Ability[/]", pokemon.Ability);
    infoTable.AddRow("[blue]Base Exp[/]", pokemon.BaseExp.ToString());

    mainTable.AddColumn(infoTable);

    // Stats section
    var statsTable = new Table().BorderColor(Color.Grey);
    statsTable.AddColumn("Stat");
    statsTable.AddColumn(new TableColumn("Value").RightAligned());
    statsTable.AddRow("HP", CreateStatBar(pokemon.Stats.HP));
    statsTable.AddRow("Attack", CreateStatBar(pokemon.Stats.Attack));
    statsTable.AddRow("Defense", CreateStatBar(pokemon.Stats.Defense));
    statsTable.AddRow("Sp. Atk", CreateStatBar(pokemon.Stats.SpAtk));
    statsTable.AddRow("Sp. Def", CreateStatBar(pokemon.Stats.SpDef));
    statsTable.AddRow("Speed", CreateStatBar(pokemon.Stats.Speed));

    mainTable.AddColumn(statsTable);

    AnsiConsole.Write(mainTable);
}

private string CreateStatBar(int value)
{
    var percentage = (double)value / 255 * 100;
    var color = percentage switch
    {
        >= 75 => "green",
        >= 50 => "yellow",
        >= 25 => "orange1",
        _ => "red"
    };
    return $"[{color}]{value}[/]";
}
```

### Tree Rendering for Hierarchical Data

```csharp
public void DisplayMapStructure(MapData map)
{
    var root = new Tree($"[yellow bold]{map.Name}[/]");
    root.Style = Style.Parse("white");

    // Add connections
    var connections = root.AddNode("[blue]Connections[/]");
    foreach (var conn in map.Connections)
    {
        connections.AddNode($"→ {conn.TargetMap} ([gray]{conn.Direction}[/])");
    }

    // Add wild Pokemon
    var wildPokemon = root.AddNode("[green]Wild Pokemon[/]");
    foreach (var encounter in map.WildEncounters.GroupBy(e => e.EncounterType))
    {
        var encounterNode = wildPokemon.AddNode($"[cyan]{encounter.Key}[/]");

        var encounterTable = new Table()
            .Border(TableBorder.None)
            .AddColumn("Pokemon")
            .AddColumn("Level")
            .AddColumn("Rate");

        foreach (var e in encounter)
        {
            encounterTable.AddRow(
                e.Pokemon.Name,
                $"{e.MinLevel}-{e.MaxLevel}",
                $"{e.Rate}%"
            );
        }

        encounterNode.AddNode(encounterTable);
    }

    // Add NPCs/Events
    var npcs = root.AddNode("[magenta]NPCs & Events[/]");
    foreach (var npc in map.NPCs)
    {
        npcs.AddNode($"[white]{npc.Name}[/] ({npc.Type})");
    }

    AnsiConsole.Write(root);
}
```

### Tree for ROM Structure Exploration

```csharp
public void DisplayRomStructure(RomData rom)
{
    var root = new Tree("[yellow bold]ROM Structure[/]");

    // Pokemon data
    var pokemonNode = root.AddNode($"[green]Pokemon Data[/] ({rom.Pokemon.Count} entries)");
    pokemonNode.AddNode($"Offset: [gray]0x{rom.Offsets.Pokemon:X6}[/]");
    pokemonNode.AddNode($"Size: [gray]{rom.Pokemon.Count * 28} bytes[/]");

    // Move data
    var movesNode = root.AddNode($"[blue]Move Data[/] ({rom.Moves.Count} entries)");
    movesNode.AddNode($"Offset: [gray]0x{rom.Offsets.Moves:X6}[/]");

    // Map data
    var mapsNode = root.AddNode($"[cyan]Map Banks[/] ({rom.MapBanks.Count} banks)");
    foreach (var bank in rom.MapBanks.Take(5))
    {
        var bankNode = mapsNode.AddNode($"Bank {bank.Id} ({bank.Maps.Count} maps)");
        foreach (var map in bank.Maps.Take(3))
        {
            bankNode.AddNode($"└─ {map.Name}");
        }
    }

    AnsiConsole.Write(root);
}
```

### Data Security: Markup Escaping

**Critical**: Always escape user input or external data:

```csharp
// ❌ DANGEROUS - Can crash if data contains [ or ]
table.AddRow(pokemon.Name);

// ✅ SAFE - Escape markup characters
table.AddRow(Markup.Escape(pokemon.Name));

// ✅ SAFE - Using interpolated markup
table.AddRow(Markup.FromInterpolated($"[green]{pokemon.Name}[/]"));
```

---

## 6. Error Handling Patterns

### Three-Tier Error Handling Strategy

#### 1. PropagateExceptions (Development/Debugging)

```csharp
#if DEBUG
app.Configure(config =>
{
    config.PropagateExceptions();
    // Exceptions bubble up - handle in try/catch
});

try
{
    return app.Run(args);
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
    return -1;
}
#endif
```

#### 2. SetExceptionHandler (Production - Custom Handling)

```csharp
app.Configure(config =>
{
    config.SetExceptionHandler((ex, resolver) =>
    {
        // Get logger if available
        var logger = resolver?.Resolve(typeof(ILogger)) as ILogger;
        logger?.LogError(ex, "Command execution failed");

        // Display user-friendly error
        AnsiConsole.MarkupLine("[red bold]Error:[/] Operation failed");

        if (ex is RomParseException romEx)
        {
            AnsiConsole.MarkupLine($"[yellow]ROM offset:[/] 0x{romEx.Offset:X6}");
            AnsiConsole.MarkupLine($"[yellow]Expected:[/] {romEx.Expected}");
            AnsiConsole.MarkupLine($"[yellow]Found:[/] {romEx.Found}");
        }
        else if (ex is ValidationException valEx)
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(valEx.Message)}[/]");
        }
        else
        {
            // Show full exception in verbose mode
            if (Environment.GetEnvironmentVariable("VERBOSE") == "true")
            {
                AnsiConsole.WriteException(ex);
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine("[gray]Run with VERBOSE=true for full details[/]");
            }
        }

        // Return appropriate exit code
        return ex is ValidationException ? 1 : -1;
    });
});
```

#### 3. Default Handler (Simple Production)

```csharp
// No special configuration - Spectre.Console.Cli handles it
// Writes friendly message and returns exit code -1
app.Configure(config =>
{
    // Just configure commands
    config.AddCommand<MyCommand>("my-command");
});

return app.Run(args); // Built-in exception handling
```

### Custom Exception Types for ROM Operations

```csharp
public class RomParseException : Exception
{
    public long Offset { get; }
    public string Expected { get; }
    public string Found { get; }

    public RomParseException(long offset, string expected, string found)
        : base($"Parse error at offset 0x{offset:X6}: Expected {expected}, found {found}")
    {
        Offset = offset;
        Expected = expected;
        Found = found;
    }
}

public class RomValidationException : Exception
{
    public string Field { get; }
    public object Value { get; }

    public RomValidationException(string field, object value, string message)
        : base(message)
    {
        Field = field;
        Value = value;
    }
}

public class DataExportException : Exception
{
    public string Format { get; }
    public string FilePath { get; }

    public DataExportException(string format, string filePath, Exception inner)
        : base($"Failed to export to {format} at {filePath}", inner)
    {
        Format = format;
        FilePath = filePath;
    }
}
```

### Error Display Helpers

```csharp
public static class ErrorDisplay
{
    public static void ShowError(string title, string message)
    {
        var panel = new Panel(Markup.Escape(message))
        {
            Header = new PanelHeader($"[red bold]{title}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("red")
        };

        AnsiConsole.Write(panel);
    }

    public static void ShowWarning(string message)
    {
        AnsiConsole.MarkupLine($"[yellow bold]⚠[/] [yellow]{Markup.Escape(message)}[/]");
    }

    public static void ShowValidationErrors(IEnumerable<ValidationError> errors)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Red)
            .AddColumn("[yellow]Field[/]")
            .AddColumn("[yellow]Error[/]");

        foreach (var error in errors)
        {
            table.AddRow(
                $"[cyan]{Markup.Escape(error.Field)}[/]",
                Markup.Escape(error.Message)
            );
        }

        AnsiConsole.Write(table);
    }
}
```

---

## 7. Testing Strategies

### Setup: Spectre.Console.Testing Package

```bash
dotnet add package Spectre.Console.Testing
dotnet add package xunit
dotnet add package FluentAssertions
```

### Testing Commands with CommandAppTester

```csharp
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using FluentAssertions;
using Xunit;

public class ParseMapCommandTests
{
    [Fact]
    public async Task Execute_ValidRomFile_ParsesSuccessfully()
    {
        // Arrange
        var app = new CommandAppTester();
        app.Configure(config =>
        {
            config.AddCommand<ParseMapCommand>("parse-map");
        });

        // Act
        var result = await app.RunAsync(new[]
        {
            "parse-map",
            "--input", "test_rom.gba",
            "--output", "./output"
        });

        // Assert
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Successfully parsed");
    }

    [Fact]
    public async Task Execute_InvalidFile_ReturnsError()
    {
        // Arrange
        var app = new CommandAppTester();
        app.Configure(config =>
        {
            config.AddCommand<ParseMapCommand>("parse-map");
        });

        // Act
        var result = await app.RunAsync(new[]
        {
            "parse-map",
            "--input", "nonexistent.gba"
        });

        // Assert
        result.ExitCode.Should().Be(-1);
        result.Output.Should().Contain("does not exist");
    }
}
```

### Testing with Dependency Injection

```csharp
public class ParseMapCommandTestsWithDI
{
    private CommandAppTester CreateApp(Action<IServiceCollection> configureServices = null)
    {
        var services = new ServiceCollection();

        // Register test doubles
        services.AddSingleton<IRomParser, MockRomParser>();
        services.AddSingleton<IDataExporter, MockDataExporter>();

        // Allow test-specific configuration
        configureServices?.Invoke(services);

        var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandAppTester(registrar);

        app.Configure(config =>
        {
            config.AddCommand<ParseMapCommand>("parse-map");
        });

        return app;
    }

    [Fact]
    public async Task Execute_WithMockParser_UsesInjectedServices()
    {
        // Arrange
        var mockParser = new Mock<IRomParser>();
        mockParser
            .Setup(p => p.ParseMapAsync(It.IsAny<string>()))
            .ReturnsAsync(new MapData { Name = "Test Map" });

        var app = CreateApp(services =>
        {
            services.AddSingleton(mockParser.Object);
        });

        // Act
        var result = await app.RunAsync(new[]
        {
            "parse-map",
            "--input", "test.gba"
        });

        // Assert
        result.ExitCode.Should().Be(0);
        mockParser.Verify(p => p.ParseMapAsync(It.IsAny<string>()), Times.Once);
    }
}
```

### Testing Settings Validation

```csharp
public class ParseMapSettingsTests
{
    [Theory]
    [InlineData("json")]
    [InlineData("csv")]
    [InlineData("binary")]
    public void Validate_ValidFormat_ReturnsSuccess(string format)
    {
        // Arrange
        var settings = new ParseMapSettings
        {
            InputFile = "test.gba", // Assume exists
            Format = format
        };

        // Act
        var result = settings.Validate();

        // Assert
        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidFormat_ReturnsError()
    {
        // Arrange
        var settings = new ParseMapSettings
        {
            InputFile = "test.gba",
            Format = "xml" // Invalid
        };

        // Act
        var result = settings.Validate();

        // Assert
        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("must be json, csv, or binary");
    }
}
```

### Testing Console Output

```csharp
public class ConsoleOutputTests
{
    [Fact]
    public void DisplayPokemonList_ValidData_OutputsFormattedTable()
    {
        // Arrange
        var console = new TestConsole();
        AnsiConsole.Console = console;

        var pokemon = new List<Pokemon>
        {
            new Pokemon { Id = 1, Name = "Bulbasaur", Type1 = PokemonType.Grass },
            new Pokemon { Id = 2, Name = "Ivysaur", Type1 = PokemonType.Grass }
        };

        // Act
        DisplayHelper.ShowPokemonList(pokemon);

        // Assert
        var output = console.Output;
        output.Should().Contain("Bulbasaur");
        output.Should().Contain("Ivysaur");
        output.Should().Contain("Grass");
    }
}
```

### Integration Testing Pattern

```csharp
[Collection("AnsiConsoleTests")] // Sequential execution for singleton AnsiConsole
public class IntegrationTests
{
    [Fact]
    public async Task FullWorkflow_ParseAndExport_CompletesSuccessfully()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var app = new CommandAppTester();
        app.Configure(config =>
        {
            config.AddCommand<ParseMapCommand>("parse-map");
            config.AddCommand<ExportDataCommand>("export");
        });

        try
        {
            // Act - Parse
            var parseResult = await app.RunAsync(new[]
            {
                "parse-map",
                "--input", "test_rom.gba",
                "--output", tempDir
            });

            // Act - Export
            var exportResult = await app.RunAsync(new[]
            {
                "export",
                "--input", Path.Combine(tempDir, "map_data.bin"),
                "--format", "json"
            });

            // Assert
            parseResult.ExitCode.Should().Be(0);
            exportResult.ExitCode.Should().Be(0);

            var exportedFile = Path.Combine(tempDir, "map_data.json");
            File.Exists(exportedFile).Should().BeTrue();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
```

### Test Helpers and Utilities

```csharp
public static class TestHelpers
{
    public static byte[] CreateMockRom()
    {
        var rom = new byte[16 * 1024 * 1024]; // 16MB
        // Add ROM header
        Array.Copy(Encoding.ASCII.GetBytes("POKEMON"), 0, rom, 0xA0, 7);
        return rom;
    }

    public static CommandAppTester CreateTestApp<TCommand>(
        Action<IServiceCollection> configureServices = null)
        where TCommand : class, ICommand
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);

        var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandAppTester(registrar);

        app.Configure(config =>
        {
            config.AddCommand<TCommand>(typeof(TCommand).Name.Replace("Command", "").ToLower());
        });

        return app;
    }
}
```

---

## 8. Project Structure Recommendations

### Recommended Directory Structure

```
Porycon3/
├── src/
│   ├── Porycon3.Cli/                    # CLI entry point
│   │   ├── Program.cs
│   │   ├── Commands/
│   │   │   ├── Pokemon/
│   │   │   │   ├── ListPokemonCommand.cs
│   │   │   │   ├── EditPokemonCommand.cs
│   │   │   │   └── AddPokemonCommand.cs
│   │   │   ├── Map/
│   │   │   │   ├── ParseMapCommand.cs
│   │   │   │   └── ExportMapCommand.cs
│   │   │   └── Data/
│   │   │       ├── ExportDataCommand.cs
│   │   │       └── ImportDataCommand.cs
│   │   ├── Settings/
│   │   │   ├── ParseMapSettings.cs
│   │   │   └── ExportDataSettings.cs
│   │   ├── Infrastructure/
│   │   │   ├── DependencyInjection/
│   │   │   │   └── ServiceCollectionExtensions.cs
│   │   │   ├── Configuration/
│   │   │   │   └── AppConfiguration.cs
│   │   │   └── Interceptors/
│   │   │       └── LoggingInterceptor.cs
│   │   └── Display/
│   │       ├── TableRenderers/
│   │       │   ├── PokemonTableRenderer.cs
│   │       │   └── MapTableRenderer.cs
│   │       └── TreeRenderers/
│   │           └── RomStructureRenderer.cs
│   │
│   ├── Porycon3.Core/                   # Business logic
│   │   ├── Parsing/
│   │   │   ├── IRomParser.cs
│   │   │   ├── GbaRomParser.cs
│   │   │   └── MapParser.cs
│   │   ├── Export/
│   │   │   ├── IDataExporter.cs
│   │   │   ├── JsonExporter.cs
│   │   │   └── CsvExporter.cs
│   │   ├── Models/
│   │   │   ├── Pokemon.cs
│   │   │   ├── MapData.cs
│   │   │   └── Move.cs
│   │   └── Validation/
│   │       └── RomValidator.cs
│   │
│   └── Porycon3.Data/                   # Data access
│       ├── Repositories/
│       │   ├── IPokemonRepository.cs
│       │   └── PokemonRepository.cs
│       └── FileSystem/
│           ├── IRomFileSystem.cs
│           └── RomFileSystem.cs
│
├── tests/
│   ├── Porycon3.Cli.Tests/
│   │   ├── Commands/
│   │   │   └── ParseMapCommandTests.cs
│   │   └── Settings/
│   │       └── ParseMapSettingsTests.cs
│   ├── Porycon3.Core.Tests/
│   │   └── Parsing/
│   │       └── GbaRomParserTests.cs
│   └── Porycon3.Integration.Tests/
│       └── FullWorkflowTests.cs
│
├── docs/
│   ├── commands/                        # Command documentation
│   ├── architecture/                    # Architecture docs
│   └── examples/                        # Usage examples
│
└── examples/
    └── sample_roms/                     # Test ROM files
```

### Program.cs Structure

```csharp
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;
using Porycon3.Cli.Infrastructure;

namespace Porycon3.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);

        // Create CommandApp with DI
        using var registrar = new DependencyInjectionRegistrar(services);
        var app = new CommandApp(registrar);

        // Configure commands
        app.Configure(config =>
        {
            config.SetApplicationName("porycon3");
            config.SetApplicationVersion("3.0.0");

#if DEBUG
            config.PropagateExceptions();
            config.ValidateExamples();
#else
            config.SetExceptionHandler(HandleException);
#endif

            // Pokemon commands
            config.AddBranch("pokemon", pokemon =>
            {
                pokemon.SetDescription("Pokemon data management");
                pokemon.AddCommand<ListPokemonCommand>("list");
                pokemon.AddCommand<EditPokemonCommand>("edit");
                pokemon.AddCommand<AddPokemonCommand>("add");
            });

            // Map commands
            config.AddBranch("map", map =>
            {
                map.SetDescription("Map data operations");
                map.AddCommand<ParseMapCommand>("parse");
                map.AddCommand<ExportMapCommand>("export");
            });

            // Data commands
            config.AddBranch("data", data =>
            {
                data.SetDescription("Data import/export");
                data.AddCommand<ExportDataCommand>("export");
                data.AddCommand<ImportDataCommand>("import");
            });
        });

        // Run application
        return await app.RunAsync(args);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Configuration
        services.AddSingleton(LoadConfiguration());

        // Core services
        services.AddSingleton<IRomParser, GbaRomParser>();
        services.AddSingleton<IMapParser, MapParser>();
        services.AddSingleton<IDataExporter, JsonExporter>();
        services.AddSingleton<IPokemonRepository, PokemonRepository>();
        services.AddSingleton<IRomFileSystem, RomFileSystem>();

        // Validation
        services.AddSingleton<IRomValidator, RomValidator>();

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    private static AppConfiguration LoadConfiguration()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        return config.Get<AppConfiguration>() ?? new AppConfiguration();
    }

    private static int HandleException(Exception ex, ITypeResolver? resolver)
    {
        var logger = resolver?.Resolve(typeof(ILogger<Program>)) as ILogger<Program>;
        logger?.LogError(ex, "Unhandled exception");

        ErrorDisplay.ShowError("Unexpected Error", ex.Message);
        return -1;
    }
}
```

---

## 9. Pokemon ROM Tool Specific Patterns

### ROM Parsing with Progress Tracking

```csharp
public class ParseRomCommand : AsyncCommand<ParseRomSettings>
{
    private readonly IRomParser _parser;

    public ParseRomCommand(IRomParser parser)
    {
        _parser = parser;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ParseRomSettings settings)
    {
        return await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
            })
            .StartAsync(async ctx =>
            {
                // Validate ROM
                var validateTask = ctx.AddTask("[yellow]Validating ROM...[/]");
                var isValid = await _parser.ValidateRomAsync(settings.RomPath);
                validateTask.Increment(100);

                if (!isValid)
                {
                    AnsiConsole.MarkupLine("[red]Invalid ROM file[/]");
                    return -1;
                }

                // Parse Pokemon
                var pokemonTask = ctx.AddTask("[green]Parsing Pokemon data[/]", maxValue: 151);
                var pokemon = await _parser.ParsePokemonAsync(
                    settings.RomPath,
                    progress => pokemonTask.Value = progress
                );

                // Parse Moves
                var movesTask = ctx.AddTask("[blue]Parsing moves[/]", maxValue: 165);
                var moves = await _parser.ParseMovesAsync(
                    settings.RomPath,
                    progress => movesTask.Value = progress
                );

                // Parse Maps
                var mapsTask = ctx.AddTask("[cyan]Parsing maps[/]", maxValue: 100);
                var maps = await _parser.ParseMapsAsync(
                    settings.RomPath,
                    progress => mapsTask.Value = progress
                );

                // Display summary
                DisplayParseSummary(pokemon.Count, moves.Count, maps.Count);

                return 0;
            });
    }

    private void DisplayParseSummary(int pokemonCount, int moveCount, int mapCount)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Green)
            .AddColumn("[yellow]Category[/]")
            .AddColumn(new TableColumn("[yellow]Count[/]").RightAligned());

        table.AddRow("Pokemon", pokemonCount.ToString());
        table.AddRow("Moves", moveCount.ToString());
        table.AddRow("Maps", mapCount.ToString());

        AnsiConsole.Write(new Panel(table)
        {
            Header = new PanelHeader("[green bold]Parse Summary[/]"),
            Border = BoxBorder.Rounded
        });
    }
}
```

### Interactive Pokemon Editor

```csharp
public class EditPokemonCommand : AsyncCommand<EditPokemonSettings>
{
    private readonly IPokemonRepository _repository;

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        EditPokemonSettings settings)
    {
        // Load Pokemon
        var pokemon = await _repository.GetByIdAsync(settings.PokemonId);
        if (pokemon == null)
        {
            AnsiConsole.MarkupLine($"[red]Pokemon #{settings.PokemonId} not found[/]");
            return -1;
        }

        // Display current data
        DisplayPokemonDetails(pokemon);

        // Interactive editing
        if (!settings.Interactive)
        {
            return await ApplySettingsAsync(pokemon, settings);
        }

        // Interactive mode
        var editName = AnsiConsole.Confirm("Edit name?", defaultValue: false);
        if (editName)
        {
            pokemon.Name = AnsiConsole.Ask<string>("Enter new name:");
        }

        var editType = AnsiConsole.Confirm("Edit type?", defaultValue: false);
        if (editType)
        {
            pokemon.Type1 = AnsiConsole.Prompt(
                new SelectionPrompt<PokemonType>()
                    .Title("Select [green]primary type[/]:")
                    .AddChoices(Enum.GetValues<PokemonType>())
            );

            var hasSecondType = AnsiConsole.Confirm("Add secondary type?");
            if (hasSecondType)
            {
                pokemon.Type2 = AnsiConsole.Prompt(
                    new SelectionPrompt<PokemonType>()
                        .Title("Select [green]secondary type[/]:")
                        .AddChoices(Enum.GetValues<PokemonType>())
                );
            }
        }

        var editStats = AnsiConsole.Confirm("Edit base stats?", defaultValue: false);
        if (editStats)
        {
            pokemon.Stats.HP = AnsiConsole.Ask<int>("HP (1-255):");
            pokemon.Stats.Attack = AnsiConsole.Ask<int>("Attack (1-255):");
            pokemon.Stats.Defense = AnsiConsole.Ask<int>("Defense (1-255):");
            pokemon.Stats.SpAtk = AnsiConsole.Ask<int>("Special Attack (1-255):");
            pokemon.Stats.SpDef = AnsiConsole.Ask<int>("Special Defense (1-255):");
            pokemon.Stats.Speed = AnsiConsole.Ask<int>("Speed (1-255):");
        }

        // Confirm changes
        AnsiConsole.WriteLine();
        DisplayPokemonDetails(pokemon);

        if (AnsiConsole.Confirm("\nSave changes?"))
        {
            await _repository.UpdateAsync(pokemon);
            AnsiConsole.MarkupLine("[green]✓ Changes saved successfully[/]");
            return 0;
        }

        AnsiConsole.MarkupLine("[yellow]Changes discarded[/]");
        return 0;
    }
}
```

### Data Export with Format Selection

```csharp
public class ExportDataCommand : AsyncCommand<ExportDataSettings>
{
    private readonly IDataExporter _exporter;
    private readonly IPokemonRepository _pokemonRepo;

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ExportDataSettings settings)
    {
        // Get export data based on type
        var data = settings.DataType switch
        {
            "pokemon" => await _pokemonRepo.GetAllAsync(),
            "moves" => await _movesRepo.GetAllAsync(),
            "items" => await _itemsRepo.GetAllAsync(),
            _ => throw new ArgumentException($"Unknown data type: {settings.DataType}")
        };

        // Export with progress
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync($"[yellow]Exporting {settings.DataType} data...[/]", async ctx =>
            {
                ctx.Status($"[yellow]Formatting as {settings.Format}...[/]");

                await _exporter.ExportAsync(
                    data,
                    settings.OutputPath,
                    settings.Format,
                    new ExportOptions
                    {
                        PrettyPrint = settings.PrettyPrint,
                        IncludeMetadata = settings.IncludeMetadata
                    }
                );
            });

        // Show success message
        var panel = new Panel($"[green]Successfully exported {data.Count} items[/]\n" +
                             $"Format: {settings.Format}\n" +
                             $"Location: {Path.GetFullPath(settings.OutputPath)}")
        {
            Header = new PanelHeader("[green bold]Export Complete[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("green")
        };

        AnsiConsole.Write(panel);

        return 0;
    }
}
```

### ROM Comparison Tool

```csharp
public class CompareRomsCommand : AsyncCommand<CompareRomsSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        CompareRomsSettings settings)
    {
        // Load both ROMs
        var rom1Data = await LoadRomDataAsync(settings.Rom1Path, "ROM 1");
        var rom2Data = await LoadRomDataAsync(settings.Rom2Path, "ROM 2");

        // Compare and display differences
        var differences = CompareRoms(rom1Data, rom2Data);

        DisplayComparisonResults(differences);

        return 0;
    }

    private void DisplayComparisonResults(RomComparisonResult result)
    {
        var tree = new Tree("[yellow bold]ROM Comparison Results[/]");

        // Pokemon differences
        if (result.PokemonDifferences.Any())
        {
            var pokemonNode = tree.AddNode($"[red]Pokemon Differences ({result.PokemonDifferences.Count})[/]");

            foreach (var diff in result.PokemonDifferences.Take(10))
            {
                var diffNode = pokemonNode.AddNode($"[cyan]#{diff.Id} - {diff.Name}[/]");

                var diffTable = new Table()
                    .Border(TableBorder.None)
                    .AddColumn("Field")
                    .AddColumn("ROM 1")
                    .AddColumn("ROM 2");

                foreach (var field in diff.ChangedFields)
                {
                    diffTable.AddRow(
                        field.FieldName,
                        $"[yellow]{field.OldValue}[/]",
                        $"[green]{field.NewValue}[/]"
                    );
                }

                diffNode.AddNode(diffTable);
            }
        }

        // Move differences
        if (result.MoveDifferences.Any())
        {
            var movesNode = tree.AddNode($"[red]Move Differences ({result.MoveDifferences.Count})[/]");
            // Similar structure...
        }

        // Summary table
        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .AddColumn("Category")
            .AddColumn(new TableColumn("Differences").RightAligned());

        summaryTable.AddRow("Pokemon", result.PokemonDifferences.Count.ToString());
        summaryTable.AddRow("Moves", result.MoveDifferences.Count.ToString());
        summaryTable.AddRow("Maps", result.MapDifferences.Count.ToString());
        summaryTable.AddRow("Items", result.ItemDifferences.Count.ToString());

        AnsiConsole.Write(new Panel(summaryTable)
        {
            Header = new PanelHeader("[blue bold]Comparison Summary[/]"),
            Border = BoxBorder.Double
        });

        AnsiConsole.Write(tree);
    }
}
```

---

## Summary: Key Takeaways

### 1. **Command Architecture**
- Use `AsyncCommand<TSettings>` for I/O-heavy operations
- Leverage hierarchical command structure with `AddBranch`
- Implement validation in `CommandSettings.Validate()`
- Use interceptors for cross-cutting concerns

### 2. **Dependency Injection**
- Use `Spectre.Console.Cli.Extensions.DependencyInjection` package
- Register services in `ServiceCollection`
- Inject via constructor parameters
- Test with mock implementations

### 3. **User Experience**
- Use `Progress` for deterministic long-running operations
- Use `Status` for indeterminate operations
- Always escape user input with `Markup.Escape()`
- Provide rich visual feedback with tables and trees

### 4. **Error Handling**
- Use `SetExceptionHandler` for production
- Create custom exception types for domain errors
- Provide helpful error messages with context
- Log errors for debugging

### 5. **Testing**
- Use `Spectre.Console.Testing` package
- Test commands with `CommandAppTester`
- Mock dependencies for unit tests
- Test console output with `TestConsole`

### 6. **ROM Tool Specific**
- Show progress for parsing operations
- Use tables for structured data display
- Use trees for hierarchical ROM structure
- Implement interactive editing with prompts
- Support multiple export formats

---

## Sources

### Official Documentation
- [Spectre.Console - Creating Commands](https://spectreconsole.net/cli/commands)
- [Spectre.Console - CommandApp](https://spectreconsole.net/cli/commandapp)
- [Spectre.Console - Progress](https://spectreconsole.net/live/progress)
- [Spectre.Console - Status](https://spectreconsole.net/live/status)
- [Spectre.Console - Tree](https://spectreconsole.net/widgets/tree)
- [Spectre.Console - Exceptions](https://spectreconsole.net/cli/exceptions)
- [Spectre.Console - Unit Testing](https://spectreconsole.net/cli/unit-testing)

### NuGet Packages
- [Spectre.Console.Cli.Extensions.DependencyInjection](https://www.nuget.org/packages/Spectre.Console.Cli.Extensions.DependencyInjection)
- [D20Tek.Spectre.Console.Extensions](https://www.nuget.org/packages/D20Tek.Spectre.Console.Extensions/)

### Community Resources
- [GitHub - agc93/spectre.cli.extensions.dependencyinjection](https://github.com/agc93/spectre.cli.extensions.dependencyinjection)
- [GitHub - d20Tek/Spectre.Console.Extensions](https://github.com/d20Tek/Spectre.Console.Extensions)
- [Code Maze - Create Better Looking Console Applications](https://code-maze.com/csharp-create-better-looking-console-applications-with-spectre-console/)
- [Coding Militia - Next level console apps](https://blog.codingmilitia.com/2021/07/27/next-level-console-apps-with-spectre-console/)
- [DarthPedro's Blog - Unit Testing Commands](https://darthpedro.net/2021/02/08/lesson-1-9-unit-testing-commands/)
- [Matthew Regis - Creating a CLI tool with Spectre.Console.Cli](https://matthewregis.dev/posts/creating-a-cli-tool-with-dotnet-and-spectre-console)

### Discussions
- [Global exception handling in cli](https://github.com/spectreconsole/spectre.console/discussions/601)
- [Dependency injection for a larger project](https://github.com/spectreconsole/spectre.console/discussions/1493)
- [UnitTesting with Spectre.Console.Cli](https://github.com/spectreconsole/spectre.console/discussions/1398)
