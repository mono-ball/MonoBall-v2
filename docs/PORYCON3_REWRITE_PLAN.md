# Porycon3: Complete C# Rewrite Plan

## Executive Summary

This document presents a comprehensive plan for rewriting the porycon2 Python tool as **Porycon3**, a modern, maintainable C# application using Spectre.Console for command-line enhancements. The new implementation will address all existing pain points while achieving 2-5x performance improvements through parallel processing and proper architecture.

---

## 🎯 Project Goals

### Primary Objectives
1. **Maintainability**: Clean architecture with < 500 lines per file
2. **Reliability**: > 90% test coverage, comprehensive validation
3. **Performance**: 2-5x faster through parallelization
4. **Usability**: Beautiful CLI with progress reporting via Spectre.Console
5. **Extensibility**: Plugin architecture for new conversion formats

### Success Criteria
- [ ] All 400+ Hoenn maps convert correctly
- [ ] Pixel-perfect output compared to Python version
- [ ] < 60 seconds for full conversion (vs ~2-3 minutes in Python)
- [ ] Zero regressions from Python implementation

---

## 📦 Solution Structure

```
Porycon3/
├── Porycon3.sln
├── src/
│   ├── Porycon3.Cli/                    # Entry point & commands
│   │   ├── Program.cs
│   │   ├── Commands/
│   │   │   ├── ConvertCommand.cs
│   │   │   ├── ExtractAudioCommand.cs
│   │   │   ├── ExtractPopupsCommand.cs
│   │   │   ├── ExtractSectionsCommand.cs
│   │   │   ├── ExtractSpritesCommand.cs
│   │   │   └── ValidateCommand.cs
│   │   ├── Settings/
│   │   │   ├── GlobalSettings.cs
│   │   │   ├── ConvertSettings.cs
│   │   │   └── ExtractSettings.cs
│   │   └── Infrastructure/
│   │       ├── TypeRegistrar.cs
│   │       └── TypeResolver.cs
│   │
│   ├── Porycon3.Core/                   # Business logic
│   │   ├── Services/
│   │   │   ├── MapConversionService.cs
│   │   │   ├── TilesetBuilderService.cs
│   │   │   ├── WorldBuilderService.cs
│   │   │   ├── AnimationScannerService.cs
│   │   │   ├── AudioConversionService.cs
│   │   │   ├── SpriteExtractorService.cs
│   │   │   └── DefinitionConversionService.cs
│   │   ├── Pipelines/
│   │   │   ├── MapConversionPipeline.cs
│   │   │   ├── TilesetBuildPipeline.cs
│   │   │   └── PipelineOptions.cs
│   │   ├── Processors/
│   │   │   ├── MetatileProcessor.cs
│   │   │   ├── TileLayerDistributor.cs
│   │   │   └── WarpResolver.cs
│   │   └── Validators/
│   │       ├── MapValidator.cs
│   │       └── TilesetValidator.cs
│   │
│   ├── Porycon3.Domain/                 # Models & Interfaces
│   │   ├── Models/
│   │   │   ├── Maps/
│   │   │   │   ├── MapDefinition.cs
│   │   │   │   ├── MapLayout.cs
│   │   │   │   ├── MapConnection.cs
│   │   │   │   └── MapEvent.cs
│   │   │   ├── Tiles/
│   │   │   │   ├── Tileset.cs
│   │   │   │   ├── Metatile.cs
│   │   │   │   ├── TileData.cs
│   │   │   │   └── Palette.cs
│   │   │   ├── Animations/
│   │   │   │   ├── TileAnimation.cs
│   │   │   │   └── AnimationFrame.cs
│   │   │   ├── Audio/
│   │   │   │   ├── AudioTrack.cs
│   │   │   │   └── AudioCategory.cs
│   │   │   └── Common/
│   │   │       ├── Position.cs
│   │   │       ├── Dimensions.cs
│   │   │       └── Region.cs
│   │   ├── Interfaces/
│   │   │   ├── IMapConverter.cs
│   │   │   ├── ITilesetBuilder.cs
│   │   │   ├── IWorldBuilder.cs
│   │   │   ├── IAudioConverter.cs
│   │   │   ├── IAnimationScanner.cs
│   │   │   ├── IMapReader.cs
│   │   │   ├── IMetatileRenderer.cs
│   │   │   └── IDefinitionConverter.cs
│   │   ├── Enums/
│   │   │   ├── MetatileLayerType.cs
│   │   │   ├── MovementBehavior.cs
│   │   │   └── OutputFormat.cs
│   │   └── Constants/
│   │       ├── TileConstants.cs
│   │       └── GbaConstants.cs
│   │
│   └── Porycon3.Infrastructure/         # External dependencies
│       ├── FileSystem/
│       │   ├── MapFileReader.cs
│       │   ├── TilesetFileReader.cs
│       │   ├── BinaryMapReader.cs
│       │   └── PaletteLoader.cs
│       ├── Serialization/
│       │   ├── TiledJsonSerializer.cs
│       │   ├── DefinitionJsonSerializer.cs
│       │   └── WorldFileSerializer.cs
│       ├── ImageProcessing/
│       │   ├── TileImageProcessor.cs
│       │   ├── MetatileRenderer.cs
│       │   └── PaletteApplicator.cs
│       ├── Audio/
│       │   ├── MidiConverter.cs
│       │   └── ExternalToolInvoker.cs
│       └── Configuration/
│           ├── ConversionOptions.cs
│           └── AnimationMappings.cs
│
├── tests/
│   ├── Porycon3.Tests.Unit/
│   │   ├── Services/
│   │   ├── Processors/
│   │   └── Models/
│   ├── Porycon3.Tests.Integration/
│   │   ├── EndToEnd/
│   │   └── Comparison/
│   └── Porycon3.Tests.Benchmarks/
│
└── docs/
    ├── architecture/
    └── migration/
```

---

## 🖥️ Command-Line Interface Design

### Commands Overview

```bash
# Main conversion command
porycon3 convert --input <pokeemerald> --output <dest> [options]

# Extraction commands
porycon3 extract audio --input <pokeemerald> --output <dest>
porycon3 extract popups --input <pokeemerald> --output <dest>
porycon3 extract sections --input <pokeemerald> --output <dest>
porycon3 extract sprites --input <pokeemerald> --output <dest>
porycon3 extract text-windows --input <pokeemerald> --output <dest>

# Utility commands
porycon3 validate --input <pokeemerald>
porycon3 list audio --input <pokeemerald>
```

### Settings Classes (Spectre.Console.Cli)

```csharp
public class GlobalSettings : CommandSettings
{
    [CommandOption("-v|--verbose")]
    [Description("Show detailed progress information")]
    public bool Verbose { get; set; }

    [CommandOption("-d|--debug")]
    [Description("Show debug information")]
    public bool Debug { get; set; }
}

public class ConvertSettings : GlobalSettings
{
    [CommandArgument(0, "<INPUT>")]
    [Description("Input directory (pokeemerald root)")]
    public string InputPath { get; set; } = "";

    [CommandArgument(1, "<OUTPUT>")]
    [Description("Output directory for converted files")]
    public string OutputPath { get; set; } = "";

    [CommandOption("-r|--region <REGION>")]
    [Description("Region name for organizing output")]
    public string? Region { get; set; }

    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output format: tiled, entity (default: entity)")]
    [DefaultValue("entity")]
    public string Format { get; set; } = "entity";

    [CommandOption("-p|--parallel <COUNT>")]
    [Description("Maximum parallel operations (default: CPU count)")]
    public int? Parallelism { get; set; }

    public override ValidationResult Validate()
    {
        if (!Directory.Exists(InputPath))
            return ValidationResult.Error($"Input directory not found: {InputPath}");

        return ValidationResult.Success();
    }
}
```

### Progress Reporting

```csharp
public async Task<int> ExecuteAsync(CommandContext ctx, ConvertSettings settings)
{
    await AnsiConsole.Progress()
        .AutoRefresh(true)
        .Columns(
            new TaskDescriptionColumn(),
            new ProgressBarColumn(),
            new PercentageColumn(),
            new RemainingTimeColumn(),
            new SpinnerColumn())
        .StartAsync(async progressCtx =>
        {
            var scanTask = progressCtx.AddTask("[cyan]Scanning maps...[/]");
            var tilesetTask = progressCtx.AddTask("[green]Building tilesets...[/]");
            var convertTask = progressCtx.AddTask("[blue]Converting maps...[/]");
            var worldTask = progressCtx.AddTask("[yellow]Generating world file...[/]");

            // Phase 1: Scan
            var maps = await _scanner.ScanMapsAsync(settings.InputPath);
            scanTask.Value = 100;

            // Phase 2: Build tilesets (parallel)
            await _tilesetBuilder.BuildAllAsync(maps, tilesetTask);

            // Phase 3: Convert maps (parallel)
            await _converter.ConvertAllAsync(maps, convertTask);

            // Phase 4: Generate world
            await _worldBuilder.BuildAsync(maps);
            worldTask.Value = 100;
        });

    // Display summary table
    DisplayConversionSummary(results);

    return 0;
}
```

---

## 🏗️ Core Domain Models

### Map Models

```csharp
/// <summary>
/// Complete map definition with all data needed for conversion.
/// </summary>
public sealed class MapDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Region { get; init; }
    public required MapLayout Layout { get; init; }
    public required TilesetReference PrimaryTileset { get; init; }
    public required TilesetReference SecondaryTileset { get; init; }
    public IReadOnlyList<MapEvent> Events { get; init; } = [];
    public IReadOnlyList<WarpEvent> Warps { get; init; } = [];
    public IReadOnlyList<MapConnection> Connections { get; init; } = [];
    public MapWeather? Weather { get; init; }
    public MapMusic? Music { get; init; }
}

public readonly record struct MapLayout(
    int Width,
    int Height,
    int BorderWidth,
    int BorderHeight,
    string LayoutId);

public sealed class TilesetReference
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public bool IsPrimary { get; init; }
}
```

### Tile Models

```csharp
/// <summary>
/// Represents a single 8x8 pixel tile with flip and palette info.
/// </summary>
public readonly record struct TileData(
    int TileId,
    int PaletteIndex,
    bool FlipHorizontal,
    bool FlipVertical)
{
    public static TileData FromRaw(ushort raw) => new(
        TileId: raw & 0x3FF,
        PaletteIndex: (raw >> 12) & 0xF,
        FlipHorizontal: (raw & 0x400) != 0,
        FlipVertical: (raw & 0x800) != 0);
}

/// <summary>
/// 16x16 metatile composed of 8 tiles (2x2 top + 2x2 bottom layers).
/// </summary>
public sealed class Metatile
{
    public required int Id { get; init; }
    public required TileData[] BottomLayer { get; init; } // 4 tiles
    public required TileData[] TopLayer { get; init; }    // 4 tiles
    public required MetatileLayerType LayerType { get; init; }
    public required int Behavior { get; init; }
    public required int TerrainType { get; init; }
}

public enum MetatileLayerType
{
    /// <summary>Bottom → Bg2 (objects), Top → Bg1 (overhead)</summary>
    Normal = 0,

    /// <summary>Bottom → Bg3 (ground), Top → Bg2 (objects)</summary>
    Covered = 1,

    /// <summary>Bottom → Bg3 (ground), Top → Bg1 (overhead)</summary>
    Split = 2
}
```

### Animation Models

```csharp
public sealed class TileAnimation
{
    public required string Id { get; init; }
    public required string Category { get; init; } // water, flower, etc.
    public required int BaseTileId { get; init; }
    public required int TileCount { get; init; }
    public required IReadOnlyList<AnimationFrame> Frames { get; init; }
    public TimeSpan FrameDuration { get; init; } = TimeSpan.FromMilliseconds(133);
    public bool IsAutomatic { get; init; } = true;
    public string? TriggerCondition { get; init; }
}

public readonly record struct AnimationFrame(
    string ImagePath,
    int FrameIndex,
    TimeSpan Duration);
```

---

## ⚙️ Service Layer Design

### IMapConversionService

```csharp
public interface IMapConversionService
{
    Task<ConversionResult> ConvertMapAsync(
        MapDefinition map,
        ConversionContext context,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ConversionResult>> ConvertAllMapsAsync(
        IEnumerable<MapDefinition> maps,
        ConversionContext context,
        IProgress<BatchProgress>? progress = null,
        CancellationToken ct = default);
}

public class MapConversionService : IMapConversionService
{
    private readonly IMetatileProcessor _metatileProcessor;
    private readonly ITileLayerDistributor _layerDistributor;
    private readonly IWarpResolver _warpResolver;
    private readonly ILogger<MapConversionService> _logger;
    private readonly ConversionOptions _options;

    public async Task<IReadOnlyList<ConversionResult>> ConvertAllMapsAsync(
        IEnumerable<MapDefinition> maps,
        ConversionContext context,
        IProgress<BatchProgress>? progress = null,
        CancellationToken ct = default)
    {
        var results = new ConcurrentBag<ConversionResult>();
        var processed = 0;
        var total = maps.Count();

        await Parallel.ForEachAsync(
            maps,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaxParallelism,
                CancellationToken = ct
            },
            async (map, token) =>
            {
                var result = await ConvertMapAsync(map, context, null, token);
                results.Add(result);

                var current = Interlocked.Increment(ref processed);
                progress?.Report(new BatchProgress(current, total, map.Name));
            });

        return results.ToList();
    }
}
```

### ITilesetBuilderService

```csharp
public interface ITilesetBuilderService
{
    void RegisterTileUsage(string tilesetName, TileData tile, MetatileLayerType layerType);

    Task<TilesetBuildResult> BuildTilesetAsync(
        string tilesetName,
        string outputPath,
        CancellationToken ct = default);

    Task<IReadOnlyList<TilesetBuildResult>> BuildAllTilesetsAsync(
        string outputPath,
        IProgress<BuildProgress>? progress = null,
        CancellationToken ct = default);

    TileMapping GetTileMapping(string tilesetName);
}

public sealed class TileMapping
{
    // Maps (original_tile_id, palette_index) → new_sequential_id
    private readonly Dictionary<(int TileId, int Palette), int> _mapping = new();

    public int GetOrAddTile(int tileId, int palette)
    {
        var key = (tileId, palette);
        if (!_mapping.TryGetValue(key, out var newId))
        {
            newId = _mapping.Count + 1; // 1-based for Tiled
            _mapping[key] = newId;
        }
        return newId;
    }
}
```

---

## 🔄 Conversion Pipeline

### TPL Dataflow Pipeline

```csharp
public class MapConversionPipeline
{
    private readonly TransformBlock<MapDefinition, LoadedMap> _loadBlock;
    private readonly TransformBlock<LoadedMap, ProcessedMap> _processBlock;
    private readonly TransformBlock<ProcessedMap, ConvertedMap> _convertBlock;
    private readonly ActionBlock<ConvertedMap> _saveBlock;

    public MapConversionPipeline(
        IMapReader reader,
        IMetatileProcessor processor,
        ITileLayerDistributor distributor,
        IMapSerializer serializer,
        PipelineOptions options)
    {
        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

        _loadBlock = new TransformBlock<MapDefinition, LoadedMap>(
            async map => await reader.LoadMapAsync(map),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = options.IoParallelism
            });

        _processBlock = new TransformBlock<LoadedMap, ProcessedMap>(
            map => processor.ProcessMetatiles(map),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = options.CpuParallelism
            });

        _convertBlock = new TransformBlock<ProcessedMap, ConvertedMap>(
            map => distributor.DistributeLayers(map),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = options.CpuParallelism
            });

        _saveBlock = new ActionBlock<ConvertedMap>(
            async map => await serializer.SaveAsync(map),
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = options.IoParallelism
            });

        _loadBlock.LinkTo(_processBlock, linkOptions);
        _processBlock.LinkTo(_convertBlock, linkOptions);
        _convertBlock.LinkTo(_saveBlock, linkOptions);
    }

    public async Task ProcessAsync(IEnumerable<MapDefinition> maps)
    {
        foreach (var map in maps)
            await _loadBlock.SendAsync(map);

        _loadBlock.Complete();
        await _saveBlock.Completion;
    }
}
```

---

## 📋 Implementation Phases

### Phase 1: Foundation (Week 1-2)
- [ ] Create solution structure with all projects
- [ ] Set up Spectre.Console.Cli with command scaffolding
- [ ] Implement dependency injection infrastructure
- [ ] Define all domain models (Maps, Tiles, Animations)
- [ ] Create core interfaces
- [ ] Set up xUnit test projects with FluentAssertions

### Phase 2: File I/O & Parsing (Week 2-3)
- [ ] Implement MapFileReader for JSON parsing
- [ ] Implement BinaryMapReader for .bin files
- [ ] Implement PaletteLoader for .gbapal.lz files
- [ ] Implement TilesetFileReader for tileset loading
- [ ] Add SixLabors.ImageSharp for image processing
- [ ] Create unit tests for all readers

### Phase 3: Core Conversion Logic (Week 3-4)
- [ ] Port MetatileProcessor from Python
- [ ] Implement TileLayerDistributor
- [ ] Port WarpResolver logic
- [ ] Implement AnimationScanner
- [ ] Create TilesetBuilderService
- [ ] Implement MapConversionService
- [ ] Add integration tests comparing to Python output

### Phase 4: Output Generation (Week 4-5)
- [ ] Implement TiledJsonSerializer
- [ ] Implement DefinitionJsonSerializer
- [ ] Implement WorldFileSerializer
- [ ] Port definition_converter.py logic
- [ ] Add tile ID remapping
- [ ] Create end-to-end conversion tests

### Phase 5: Audio & Extras (Week 5-6)
- [ ] Port AudioConverter with external tool invocation
- [ ] Implement PopupExtractor
- [ ] Implement SectionExtractor
- [ ] Implement SpriteExtractor
- [ ] Implement TextWindowExtractor
- [ ] Add command-line commands for each

### Phase 6: Performance & Polish (Week 6-7)
- [ ] Implement parallel processing pipeline
- [ ] Add memory pooling for hot paths
- [ ] Performance benchmarks vs Python
- [ ] Error handling improvements
- [ ] Logging and diagnostics
- [ ] CLI UX polish (colors, progress, tables)

### Phase 7: Testing & Documentation (Week 7-8)
- [ ] Achieve > 90% code coverage
- [ ] Add performance regression tests
- [ ] Complete Python comparison tests
- [ ] Write migration documentation
- [ ] Create user guide
- [ ] Package for distribution

---

## 📊 Key Metrics & Targets

| Metric | Python (Current) | C# Target |
|--------|------------------|-----------|
| Full conversion time | ~120-180s | < 60s |
| Memory usage | ~500MB peak | < 200MB |
| Maps per second | ~3-5 | 15-20 |
| Lines of code | ~11,500 | ~8,000 |
| Files per module | 1 large | Many small |
| Test coverage | 0% | > 90% |
| Type safety | Dict[str, Any] | Full typing |

---

## 🔧 Technology Stack

### Core Dependencies
- **.NET 8** - Latest LTS with performance improvements
- **Spectre.Console** 0.49+ - Beautiful console output
- **Spectre.Console.Cli** - Command-line parsing
- **SixLabors.ImageSharp** - Cross-platform image processing
- **System.Text.Json** - High-performance JSON
- **Microsoft.Extensions.DependencyInjection** - DI container
- **Microsoft.Extensions.Logging** - Structured logging
- **System.IO.Abstractions** - Testable file I/O
- **System.Threading.Tasks.Dataflow** - Pipeline processing

### Testing Dependencies
- **xUnit** - Test framework
- **FluentAssertions** - Readable assertions
- **NSubstitute** - Mocking
- **BenchmarkDotNet** - Performance testing
- **Verify** - Snapshot testing

---

## 🚀 Quick Start (After Implementation)

```bash
# Install globally
dotnet tool install --global porycon3

# Convert all maps
porycon3 convert ./pokeemerald ./output --verbose

# Extract audio with progress
porycon3 extract audio ./pokeemerald ./output/Audio

# Validate input before conversion
porycon3 validate ./pokeemerald

# Show help
porycon3 --help
```

---

## 📝 Migration Notes

### Breaking Changes from porycon2
1. Command syntax changed (positional args, subcommands)
2. Configuration via appsettings.json instead of constants.py
3. Output paths slightly restructured
4. Debug output format changed

### Compatibility
- Output format fully compatible with existing PokeSharp
- All existing maps will convert identically
- Animation definitions unchanged

---

## ✅ Definition of Done

A feature is complete when:
1. [ ] Unit tests pass with > 90% coverage
2. [ ] Integration tests comparing to Python output pass
3. [ ] No performance regression vs Python
4. [ ] Code reviewed and approved
5. [ ] Documentation updated
6. [ ] No compiler warnings
7. [ ] Follows established patterns

---

*This plan was generated by the Hive Mind collective intelligence system analyzing the porycon2 codebase and synthesizing best practices for modern C# development.*
