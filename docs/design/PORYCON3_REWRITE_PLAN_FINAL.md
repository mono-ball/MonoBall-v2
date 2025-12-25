# Porycon3: Pragmatic Rewrite Plan

## Executive Summary

This plan addresses the lessons learned from the failed Porycon3 attempt (which produced 5,828 lines of documentation but only 53 bytes of code) and the buggy porycon2 Python codebase (11,575 lines with 0% test coverage).

**The key insight**: Start with working code, not perfect architecture.

---

## Critical Lessons from Failed Porycon3 Attempt

| What Happened | The Fix |
|---------------|---------|
| 5,828 lines of architecture docs before 1 line of code | Code first, document as you go |
| Planned 5 projects before implementing features | Start with 1 project, split later |
| 7-phase waterfall plan | Iterative 3-phase approach |
| TPL Dataflow pipelines before basic conversion worked | Get basic conversion working first |
| Zero validation against Python output | Validate after every feature |

---

## The 3-Phase Pragmatic Approach

### Phase 1: Proof of Concept (3-5 days)
**Goal**: Convert ONE map successfully

```
Porycon3/
├── Porycon3.csproj         # Single project
├── Program.cs              # Entry point
├── Converter.cs            # Monolithic converter (OK for now)
├── Models/                 # Basic domain models
│   ├── MapData.cs
│   ├── TileData.cs
│   └── Metatile.cs
└── test_output/            # Validation against Python
```

**Tasks**:
1. Create single console project with Spectre.Console
2. Implement basic map.json reader
3. Implement basic metatile.bin reader
4. Convert Route 101 map
5. **Validate output matches Python exactly**

**Success Criteria**:
- Route 101 converts successfully
- Output JSON matches porycon2 output
- Basic progress display works

### Phase 2: Test + Refactor (3-5 days)
**Goal**: Add tests, extract services, prove it scales

```
Porycon3/
├── src/
│   └── Porycon3/
│       ├── Program.cs
│       ├── Commands/
│       │   └── ConvertCommand.cs
│       ├── Services/
│       │   ├── MapConverter.cs
│       │   ├── TilesetBuilder.cs
│       │   └── MetatileProcessor.cs
│       └── Models/
└── tests/
    └── Porycon3.Tests/
        ├── MapConverterTests.cs
        └── ComparisonTests.cs
```

**Tasks**:
1. Add xUnit test project
2. Create comparison tests (C# output vs Python output)
3. Extract services from monolithic converter
4. Test with 5 more diverse maps
5. Fix any discrepancies

**Success Criteria**:
- 6 maps convert correctly
- All comparison tests pass
- Test coverage > 50%

### Phase 3: Features + Scale (1-2 weeks)
**Goal**: All features working, full test coverage

```
Porycon3/
├── src/
│   ├── Porycon3.Cli/          # CLI commands
│   ├── Porycon3.Core/         # Business logic
│   └── Porycon3.Infrastructure/ # File I/O
└── tests/
    ├── Porycon3.Tests.Unit/
    └── Porycon3.Tests.Integration/
```

**Tasks**:
1. Add all extraction commands (audio, sprites, etc.)
2. Implement parallel processing
3. Add remaining edge cases
4. Performance optimization
5. Polish CLI experience

**Success Criteria**:
- All 400+ maps convert correctly
- < 60 second full conversion time
- > 90% test coverage
- Zero regressions from Python

---

## Technology Stack

### Core (Phase 1+)
```xml
<PackageReference Include="Spectre.Console" Version="0.49.*" />
<PackageReference Include="Spectre.Console.Cli" Version="0.49.*" />
<PackageReference Include="SixLabors.ImageSharp" Version="3.*" />
```

### Testing (Phase 2+)
```xml
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="FluentAssertions" Version="7.*" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
```

### Infrastructure (Phase 3)
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.*" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.*" />
<PackageReference Include="System.IO.Abstractions" Version="21.*" />
```

---

## Implementation Details

### Phase 1 Implementation

#### Step 1: Project Setup
```bash
dotnet new console -n Porycon3 -f net9.0
cd Porycon3
dotnet add package Spectre.Console
dotnet add package Spectre.Console.Cli
dotnet add package SixLabors.ImageSharp
```

#### Step 2: Basic Program.cs
```csharp
using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<ConvertCommand>();
return app.Run(args);

public class ConvertSettings : CommandSettings
{
    [CommandArgument(0, "<INPUT>")]
    public string InputPath { get; set; } = "";

    [CommandArgument(1, "<OUTPUT>")]
    public string OutputPath { get; set; } = "";

    [CommandOption("-m|--map <MAP>")]
    public string? MapName { get; set; }
}

public class ConvertCommand : Command<ConvertSettings>
{
    public override int Execute(CommandContext context, ConvertSettings settings)
    {
        AnsiConsole.MarkupLine("[cyan]Porycon3[/] - Pokemon Map Converter");

        var converter = new Converter(settings.InputPath, settings.OutputPath);

        if (!string.IsNullOrEmpty(settings.MapName))
        {
            converter.ConvertMap(settings.MapName);
        }
        else
        {
            converter.ConvertAll();
        }

        return 0;
    }
}
```

#### Step 3: Minimal Converter
```csharp
public class Converter
{
    private readonly string _inputDir;
    private readonly string _outputDir;

    public Converter(string inputDir, string outputDir)
    {
        _inputDir = inputDir;
        _outputDir = outputDir;
    }

    public void ConvertMap(string mapName)
    {
        AnsiConsole.Status()
            .Start($"Converting {mapName}...", ctx =>
            {
                // 1. Read map.json
                var mapJson = ReadMapJson(mapName);

                // 2. Read metatile data
                var metatiles = ReadMetatiles(mapJson.Layout.PrimaryTileset,
                                               mapJson.Layout.SecondaryTileset);

                // 3. Read map.bin
                var mapData = ReadMapBin(mapName, mapJson.Layout.Width, mapJson.Layout.Height);

                // 4. Process metatiles into layers
                var layers = ProcessLayers(mapData, metatiles);

                // 5. Write output
                WriteOutput(mapName, layers);
            });

        AnsiConsole.MarkupLine($"[green]✓[/] Converted {mapName}");
    }

    // Start simple, refactor later
    private MapJson ReadMapJson(string mapName) { /* ... */ }
    private List<Metatile> ReadMetatiles(string primary, string secondary) { /* ... */ }
    private ushort[] ReadMapBin(string mapName, int width, int height) { /* ... */ }
    private LayerData ProcessLayers(ushort[] mapData, List<Metatile> metatiles) { /* ... */ }
    private void WriteOutput(string mapName, LayerData layers) { /* ... */ }
}
```

### Key Algorithm: Metatile to GID Conversion

This is the most critical part of the conversion and needs to match Python exactly:

```csharp
public class MetatileProcessor
{
    /// <summary>
    /// GBA metatiles use 3-layer distribution based on layer type:
    /// - Type 0 (Normal): Bottom → Bg2, Top → Bg1
    /// - Type 1 (Covered): Bottom → Bg3, Top → Bg2
    /// - Type 2 (Split): Bottom → Bg3, Top → Bg1
    /// </summary>
    public LayerDistribution DistributeMetatile(Metatile metatile)
    {
        var layerType = (MetatileLayerType)(metatile.Behavior >> 5 & 0x3);

        return layerType switch
        {
            MetatileLayerType.Normal => new LayerDistribution(
                Bg3: Array.Empty<TileData>(),
                Bg2: metatile.BottomTiles,
                Bg1: metatile.TopTiles),

            MetatileLayerType.Covered => new LayerDistribution(
                Bg3: metatile.BottomTiles,
                Bg2: metatile.TopTiles,
                Bg1: Array.Empty<TileData>()),

            MetatileLayerType.Split => new LayerDistribution(
                Bg3: metatile.BottomTiles,
                Bg2: Array.Empty<TileData>(),
                Bg1: metatile.TopTiles),

            _ => throw new InvalidOperationException($"Unknown layer type: {layerType}")
        };
    }
}

public record LayerDistribution(
    TileData[] Bg3,  // Ground layer (under objects)
    TileData[] Bg2,  // Object layer (player/NPCs)
    TileData[] Bg1   // Overhead layer (treetops, roofs)
);
```

### Key Algorithm: Tile ID Remapping

```csharp
public class TilesetBuilder
{
    private readonly Dictionary<(int TileId, int Palette, bool FlipH, bool FlipV), int> _tileMapping = new();
    private int _nextGid = 1;

    /// <summary>
    /// Maps original tile data to sequential Tiled GIDs.
    /// Deduplicates identical tile+palette+flip combinations.
    /// </summary>
    public int GetOrCreateGid(TileData tile)
    {
        var key = (tile.TileId, tile.PaletteIndex, tile.FlipH, tile.FlipV);

        if (!_tileMapping.TryGetValue(key, out var gid))
        {
            gid = _nextGid++;
            _tileMapping[key] = gid;
        }

        return gid;
    }

    /// <summary>
    /// Builds tileset image from used tiles.
    /// </summary>
    public Image<Rgba32> BuildTilesetImage(
        Image<Rgba32> sourceTiles,
        Palette[] palettes,
        int columns = 16)
    {
        var tilesCount = _tileMapping.Count;
        var rows = (tilesCount + columns - 1) / columns;
        var image = new Image<Rgba32>(columns * 8, rows * 8);

        foreach (var (key, gid) in _tileMapping.OrderBy(x => x.Value))
        {
            var srcTile = ExtractTile(sourceTiles, key.TileId);
            var coloredTile = ApplyPalette(srcTile, palettes[key.Palette]);

            if (key.FlipH) coloredTile.Mutate(x => x.Flip(FlipMode.Horizontal));
            if (key.FlipV) coloredTile.Mutate(x => x.Flip(FlipMode.Vertical));

            var destX = ((gid - 1) % columns) * 8;
            var destY = ((gid - 1) / columns) * 8;

            image.Mutate(x => x.DrawImage(coloredTile, new Point(destX, destY), 1f));
        }

        return image;
    }
}
```

---

## Validation Strategy

### Comparison Testing

```csharp
public class ComparisonTests
{
    private readonly string _pythonOutputDir = "../porycon2/output";
    private readonly string _csharpOutputDir = "../Porycon3/output";

    [Theory]
    [InlineData("ROUTE101")]
    [InlineData("LITTLEROOT_TOWN")]
    [InlineData("PETALBURG_CITY")]
    [InlineData("RUSTBORO_CITY_GYM")]
    [InlineData("METEOR_FALLS_1F_1R")]
    public void ConvertedMap_MatchesPythonOutput(string mapName)
    {
        // Arrange
        var pythonPath = Path.Combine(_pythonOutputDir, $"{mapName}.json");
        var csharpPath = Path.Combine(_csharpOutputDir, $"{mapName}.json");

        // Act
        var converter = new Converter(_inputDir, _csharpOutputDir);
        converter.ConvertMap(mapName);

        // Assert
        var pythonJson = File.ReadAllText(pythonPath);
        var csharpJson = File.ReadAllText(csharpPath);

        // Compare layer data, ignoring formatting differences
        var pythonData = JsonSerializer.Deserialize<MapOutput>(pythonJson);
        var csharpData = JsonSerializer.Deserialize<MapOutput>(csharpJson);

        csharpData.Layers.Should().BeEquivalentTo(pythonData.Layers);
        csharpData.Warps.Should().BeEquivalentTo(pythonData.Warps);
        csharpData.Events.Should().BeEquivalentTo(pythonData.Events);
    }

    [Fact]
    public void TilesetImage_MatchesPythonOutput()
    {
        var pythonTileset = Image.Load<Rgba32>("../porycon2/output/general.png");
        var csharpTileset = Image.Load<Rgba32>("./output/general.png");

        // Pixel-perfect comparison
        for (int y = 0; y < pythonTileset.Height; y++)
        for (int x = 0; x < pythonTileset.Width; x++)
        {
            csharpTileset[x, y].Should().Be(pythonTileset[x, y],
                $"Pixel mismatch at ({x}, {y})");
        }
    }
}
```

---

## CLI Design

### Commands Structure

```
porycon3 convert <input> <output> [options]
  -m, --map <MAP>           Convert single map
  -r, --region <REGION>     Output region name
  -f, --format <FORMAT>     Output format (tiled|entity)
  -p, --parallel <COUNT>    Parallel workers
  -v, --verbose             Verbose output

porycon3 extract <type> <input> <output>
  Types: audio, sprites, popups, sections, text-windows

porycon3 validate <input>
  Validates input directory structure

porycon3 compare <python-output> <csharp-output>
  Compares outputs for testing
```

### Progress Display

```csharp
await AnsiConsole.Progress()
    .AutoRefresh(true)
    .Columns(
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new RemainingTimeColumn())
    .StartAsync(async ctx =>
    {
        var scanTask = ctx.AddTask("[cyan]Scanning maps[/]", maxValue: 1);
        var convertTask = ctx.AddTask("[green]Converting[/]", maxValue: maps.Count);
        var tilesetTask = ctx.AddTask("[yellow]Building tilesets[/]", maxValue: tilesets.Count);

        // Scan
        var maps = await ScanMapsAsync();
        scanTask.Increment(1);

        // Convert in parallel
        await Parallel.ForEachAsync(maps, async (map, ct) =>
        {
            await ConvertMapAsync(map, ct);
            convertTask.Increment(1);
        });

        // Build tilesets
        foreach (var tileset in tilesets)
        {
            await BuildTilesetAsync(tileset);
            tilesetTask.Increment(1);
        }
    });
```

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| Python behavior not fully understood | Run Python with debug output, diff outputs |
| Edge cases in metatile processing | Extensive comparison testing |
| ImageSharp behavior differs from PIL | Pixel-perfect image comparison tests |
| Performance regression | BenchmarkDotNet suite, target < 60s |
| Scope creep | Strict phase gates, validate before next phase |

---

## Definition of Done

### Per-Feature
- [ ] Comparison test passes (vs Python)
- [ ] Unit test coverage > 80%
- [ ] No compiler warnings
- [ ] Code reviewed

### Per-Phase
- [ ] All phase tasks complete
- [ ] All comparison tests pass
- [ ] Performance within targets
- [ ] User acceptance (run against full pokeemerald)

### Project Complete
- [ ] All 400+ maps convert correctly
- [ ] All extraction commands work
- [ ] Full test coverage > 90%
- [ ] Documentation complete
- [ ] Packaged as dotnet tool

---

## Timeline

| Phase | Duration | Milestone |
|-------|----------|-----------|
| Phase 1 | 3-5 days | Route 101 converts correctly |
| Phase 2 | 3-5 days | 6 maps + tests + refactor |
| Phase 3 | 1-2 weeks | All features, all maps, full coverage |
| **Total** | **2-4 weeks** | Production-ready Porycon3 |

---

## Next Steps

1. **Today**: Create Porycon3 project, add packages
2. **Day 1-2**: Implement basic map.json and metatile.bin readers
3. **Day 2-3**: Implement minimal converter for Route 101
4. **Day 3-4**: Validate output matches Python
5. **Day 4-5**: Fix discrepancies, complete Phase 1

Start simple. Validate constantly. Refactor with confidence.
