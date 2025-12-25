# Porycon2 Python to C# Rewrite Analysis

## Executive Summary

This document provides a comprehensive analysis of the porycon2 Python codebase (25 modules, ~400KB total code) for conversion to C#. The codebase converts Pokemon Emerald ROM data to Tiled map format and PokeSharp game definitions.

**Key Findings:**
- **Total Complexity:** ~2,000 lines in converter.py alone, 25 modules total
- **Main Dependencies:** Pillow (image processing), struct (binary parsing), json
- **Architecture Pattern:** Procedural Python with heavy dict/list manipulation
- **Target Improvement:** Strong typing, LINQ, async/parallel processing, proper OOP

---

## Module-by-Module Analysis

### 1. converter.py (95KB / ~2,036 lines) - CORE CONVERSION ENGINE

**Purpose:** Main orchestrator for converting Pokemon Emerald maps to Tiled format.

#### Current Pain Points:
1. **Massive god class** - `MapConverter` does everything
2. **Dict/tuple hell** - Complex nested dicts with tuple keys like `(tile_id, palette, layer_type)`
3. **No type safety** - Everything is `Dict[str, Any]` or `Optional[Dict]`
4. **Sequential processing** - No parallelization despite independent map conversions
5. **Manual memory management** - Tracking `next_gid`, deduplication via `image_to_gid`
6. **Binary file parsing** - Uses raw `struct.unpack` everywhere
7. **Mixed concerns** - Handles reading, processing, rendering, and saving

#### Key Data Structures (Convert to C# classes):

```python
# Current: Messy tuple keys
used_metatiles: Dict[Tuple[int, str, int], Tuple[Image.Image, Image.Image]]
metatile_to_gid: Dict[Tuple[int, str, int, bool], int]
tile_id_to_gids: Dict[Tuple[int, str], List[Tuple[int, Tuple[int, str, int], int]]]

# Should become C# with proper classes:
class MetatileKey {
    int MetatileId { get; set; }
    string TilesetName { get; set; }
    int LayerTypeValue { get; set; }
}

class MetatileImages {
    Image BottomLayer { get; set; }
    Image TopLayer { get; set; }
}

Dictionary<MetatileKey, MetatileImages> UsedMetatiles;
Dictionary<MetatileGidKey, int> MetatileToGid;
```

#### C# Improvements:

1. **Break into smaller services:**
   ```csharp
   // Dependency Injection pattern
   public class MapConverter {
       private readonly IMapReader _mapReader;
       private readonly ITilesetBuilder _tilesetBuilder;
       private readonly IMetatileRenderer _renderer;
       private readonly IMetatileProcessor _processor;
       private readonly IAnimationScanner _animations;
   }
   ```

2. **Strongly typed models:**
   ```csharp
   public record MapConversionContext(
       string MapId,
       string Region,
       TilesetData Tilesets,
       MapDimensions Dimensions
   );

   public record TilesetData(
       string PrimaryTileset,
       string SecondaryTileset,
       IReadOnlyList<MetatileWithAttributes> PrimaryMetatiles,
       IReadOnlyList<MetatileWithAttributes> SecondaryMetatiles,
       IReadOnlyDictionary<int, int> PrimaryAttributes,
       IReadOnlyDictionary<int, int> SecondaryAttributes
   );
   ```

3. **Async/parallel processing:**
   ```csharp
   public async Task<IEnumerable<ConversionResult>> ConvertMapsAsync(
       IEnumerable<string> mapIds,
       CancellationToken ct)
   {
       var tasks = mapIds.Select(id => ConvertMapAsync(id, ct));
       return await Task.WhenAll(tasks);
   }
   ```

4. **LINQ for metatile processing:**
   ```csharp
   // Instead of nested loops
   var usedGids = borderGids.Values
       .Where(gid => gid > 0)
       .Concat(layerData.SelectMany(layer => layer.Where(gid => gid > 0)))
       .ToHashSet();
   ```

5. **Span<T> for binary data:**
   ```csharp
   public ReadOnlySpan<ushort> ReadMapBin(string path, int width, int height) {
       var bytes = File.ReadAllBytes(path);
       return MemoryMarshal.Cast<byte, ushort>(bytes);
   }
   ```

---

### 2. definition_converter.py (48KB / ~1,200 lines) - DTO TRANSFORMATION

**Purpose:** Converts Tiled maps to PokeSharp Definition JSON format (EF Core compatible).

#### Current Pain Points:
1. **Manual JSON building** - Tedious dict construction
2. **String concatenation for IDs** - Error-prone ID generation
3. **No validation** - Silent failures on malformed data
4. **Repeated property extraction** - Same pattern for every object type
5. **Base64 encoding** - Manual byte conversion for tile data

#### Key Transformations:

```python
# Input: Tiled JSON (dict-based)
tiled_map = {
    "properties": [...],
    "layers": [...],
    "tilesets": [...]
}

# Output: PokeSharp Definition JSON (entity-shaped)
definition = {
    "mapId": "base:map:hoenn/littleroot_town",
    "layers": [...],
    "warps": [...],
    "npcs": [...]
}
```

#### C# Improvements:

1. **Use AutoMapper or Mapperly:**
   ```csharp
   public class TiledToDefinitionProfile : Profile {
       public TiledToDefinitionProfile() {
           CreateMap<TiledMap, MapEntity>()
               .ForMember(dest => dest.MapId, opt => opt.MapFrom(src =>
                   IdTransformer.MapId(src.Name, src.Region)))
               .ForMember(dest => dest.Layers, opt => opt.MapFrom(src =>
                   src.Layers.Where(l => l.Type == "tilelayer")));
       }
   }
   ```

2. **Strongly typed DTOs:**
   ```csharp
   public record TiledMapDto {
       public int Width { get; init; }
       public int Height { get; init; }
       public List<TiledProperty> Properties { get; init; } = new();
       public List<TiledLayer> Layers { get; init; } = new();
   }

   public record MapEntityDto {
       [Required]
       public string MapId { get; init; } = string.Empty;
       public string Name { get; init; } = string.Empty;
       public int Width { get; init; }
       public int Height { get; init; }
       public List<MapLayerDto> Layers { get; init; } = new();
       public List<WarpDto> Warps { get; init; } = new();
   }
   ```

3. **Validation with FluentValidation:**
   ```csharp
   public class MapEntityValidator : AbstractValidator<MapEntityDto> {
       public MapEntityValidator() {
           RuleFor(x => x.MapId).NotEmpty().Matches(ID_PATTERN);
           RuleFor(x => x.Width).GreaterThan(0);
           RuleFor(x => x.Height).GreaterThan(0);
       }
   }
   ```

4. **Source generators for boilerplate:**
   ```csharp
   [AutoConstructor] // Use source generator
   public partial class DefinitionConverter {
       private readonly ILogger<DefinitionConverter> _logger;
       private readonly IIdTransformer _idTransformer;
       private readonly IMapper _mapper;
   }
   ```

---

### 3. audio_converter.py (36KB / ~900 lines) - EXTERNAL TOOL INVOCATION

**Purpose:** Converts MIDI/audio files using external tools (mid2agb, gbamusriper).

#### Current Pain Points:
1. **Process spawning everywhere** - `subprocess.run()` with no error handling
2. **Path hell** - Manual path construction and validation
3. **No retry logic** - Fails silently on tool errors
4. **Hardcoded tool paths** - Not configurable
5. **No progress tracking** - Blocking operations with no feedback
6. **Mixed concerns** - Handles both MIDI conversion AND music categorization

#### Current Pattern:

```python
def convert_midi_to_s(self, midi_path, output_s_path):
    cmd = [self.mid2agb_path, str(midi_path), "-o", str(output_s_path)]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        logger.error(f"mid2agb failed: {result.stderr}")
        return False
    return True
```

#### C# Improvements:

1. **Process wrapper with better error handling:**
   ```csharp
   public class ExternalToolExecutor {
       public async Task<ProcessResult> ExecuteAsync(
           ProcessStartInfo startInfo,
           TimeSpan timeout,
           IProgress<string>? progress = null,
           CancellationToken ct = default)
       {
           using var process = new Process { StartInfo = startInfo };
           var outputBuilder = new StringBuilder();
           var errorBuilder = new StringBuilder();

           process.OutputDataReceived += (s, e) => {
               if (e.Data != null) {
                   outputBuilder.AppendLine(e.Data);
                   progress?.Report(e.Data);
               }
           };

           process.ErrorDataReceived += (s, e) => {
               if (e.Data != null) errorBuilder.AppendLine(e.Data);
           };

           process.Start();
           process.BeginOutputReadLine();
           process.BeginErrorReadLine();

           using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
           cts.CancelAfter(timeout);

           await process.WaitForExitAsync(cts.Token);

           return new ProcessResult(
               process.ExitCode,
               outputBuilder.ToString(),
               errorBuilder.ToString()
           );
       }
   }
   ```

2. **Configuration-based tool paths:**
   ```csharp
   public class AudioConverterOptions {
       public string Mid2AgbPath { get; set; } = "mid2agb";
       public string GbamusriperPath { get; set; } = "gbamusriper";
       public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
       public int MaxRetries { get; set; } = 3;
   }

   // In Startup.cs
   services.Configure<AudioConverterOptions>(
       Configuration.GetSection("AudioConverter"));
   ```

3. **Retry with Polly:**
   ```csharp
   private readonly IAsyncPolicy<ProcessResult> _retryPolicy = Policy
       .HandleResult<ProcessResult>(r => r.ExitCode != 0)
       .WaitAndRetryAsync(3, retryAttempt =>
           TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

   public async Task<ProcessResult> ConvertWithRetryAsync(
       string inputPath,
       string outputPath,
       CancellationToken ct)
   {
       return await _retryPolicy.ExecuteAsync(async () =>
           await _executor.ExecuteAsync(
               CreateMid2AgbProcess(inputPath, outputPath),
               _options.Timeout,
               ct: ct
           )
       );
   }
   ```

4. **Parallel batch processing:**
   ```csharp
   public async Task<ConversionResults> ConvertBatchAsync(
       IEnumerable<string> midiFiles,
       string outputDir,
       IProgress<ConversionProgress>? progress = null,
       CancellationToken ct = default)
   {
       var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
       var results = new ConcurrentBag<ConversionResult>();

       var tasks = midiFiles.Select(async file => {
           await semaphore.WaitAsync(ct);
           try {
               var result = await ConvertSingleAsync(file, outputDir, ct);
               results.Add(result);
               progress?.Report(new ConversionProgress(results.Count, midiFiles.Count()));
           }
           finally {
               semaphore.Release();
           }
       });

       await Task.WhenAll(tasks);
       return new ConversionResults(results);
   }
   ```

---

### 4. animation_scanner.py (35KB / ~805 lines) - ANIMATION EXTRACTION

**Purpose:** Scans animation folders and extracts frame data with timing information.

#### Current Pain Points:
1. **Hardcoded animation mappings** - Giant dict at top of file (~290 lines)
2. **Regex parsing of C code** - Fragile parsing of `tileset_anims.c`
3. **Manual frame extraction** - Complex image slicing logic
4. **No caching** - Reparses same files repeatedly
5. **Mixed tile sizes** - Handles both 8x8 tiles and 16x16 metatiles inconsistently

#### Current Hardcoded Data:

```python
ANIMATION_MAPPINGS = {
    "general": {
        "flower": {
            "base_tile_id": 508,
            "num_tiles": 4,
            "anim_folder": "flower",
            "duration_ms": 133,
            "frame_sequence": [0, 1, 0, 2]
        },
        # ... 50+ more entries
    }
}
```

#### C# Improvements:

1. **Move config to JSON/YAML:**
   ```csharp
   public record AnimationDefinition {
       public int BaseTileId { get; init; }
       public int NumTiles { get; init; }
       public string AnimFolder { get; init; } = string.Empty;
       public int DurationMs { get; init; }
       public int[]? FrameSequence { get; init; }
       public bool IsSecondary { get; init; }
   }

   // animations.json
   {
       "general": {
           "flower": {
               "baseTileId": 508,
               "numTiles": 4,
               "animFolder": "flower",
               "durationMs": 133,
               "frameSequence": [0, 1, 0, 2]
           }
       }
   }
   ```

2. **Use Roslyn for C# parsing instead of regex:**
   ```csharp
   public class TilesetAnimsParser {
       public async Task<Dictionary<string, AnimationTiming>> ParseAsync(
           string filePath,
           CancellationToken ct)
       {
           var tree = CSharpSyntaxTree.ParseText(
               await File.ReadAllTextAsync(filePath, ct));

           var root = await tree.GetRootAsync(ct);

           // Use syntax walker to extract animation arrays and timing
           var visitor = new AnimationSyntaxVisitor();
           visitor.Visit(root);

           return visitor.Animations;
       }
   }
   ```

3. **Image processing with ImageSharp (faster than System.Drawing):**
   ```csharp
   public IReadOnlyList<Image<Rgba32>> ExtractTilesFromFrame(
       string framePath,
       int baseTileId,
       int numTiles,
       int tileSize = 8)
   {
       using var frameImg = Image.Load<Rgba32>(framePath);

       var tiles = new List<Image<Rgba32>>(numTiles);
       var tilesPerRow = frameImg.Width / tileSize;

       for (int i = 0; i < numTiles; i++) {
           var col = i % tilesPerRow;
           var row = i / tilesPerRow;
           var rect = new Rectangle(col * tileSize, row * tileSize, tileSize, tileSize);

           var tile = frameImg.Clone(ctx => ctx.Crop(rect));
           tiles.Add(tile);
       }

       return tiles;
   }
   ```

4. **Caching with MemoryCache:**
   ```csharp
   public class AnimationScanner {
       private readonly IMemoryCache _cache;
       private readonly MemoryCacheEntryOptions _cacheOptions = new() {
           AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
       };

       public async Task<AnimationData> GetAnimationsAsync(
           string tilesetName,
           CancellationToken ct)
       {
           var cacheKey = $"animations:{tilesetName}";

           if (_cache.TryGetValue(cacheKey, out AnimationData? cached))
               return cached!;

           var data = await ExtractAnimationsAsync(tilesetName, ct);
           _cache.Set(cacheKey, data, _cacheOptions);

           return data;
       }
   }
   ```

---

### 5. tileset_builder.py (27KB / ~528 lines) - TILESET ASSEMBLY

**Purpose:** Builds complete tilesets by collecting used tiles from maps.

#### Current Pain Points:
1. **Tracking used tiles via sets** - Manual `set` management
2. **Palette application logic** - Complex primary/secondary palette merging
3. **No tile deduplication strategy** - Inefficient duplicate storage
4. **Manual image composition** - Lots of PIL boilerplate
5. **GID calculation everywhere** - Tiled GID logic scattered throughout

#### Current Pattern:

```python
class TilesetBuilder:
    def __init__(self, input_dir: str):
        self.used_tiles: Dict[str, Set[int]] = {}
        self.used_tiles_with_palettes: Dict[str, Set[Tuple[int, int]]] = {}
        self.tileset_relationships: Dict[str, Set[Tuple[str, str]]] = {}
```

#### C# Improvements:

1. **Use HashSet<T> with proper equality:**
   ```csharp
   public record TileKey(int TileId, int PaletteIndex);

   public class TilesetBuilder {
       private readonly Dictionary<string, HashSet<TileKey>> _usedTiles = new();
       private readonly Dictionary<string, HashSet<TilesetPair>> _relationships = new();

       public void AddTiles(string tilesetName, IEnumerable<TileKey> tiles) {
           if (!_usedTiles.TryGetValue(tilesetName, out var set)) {
               set = new HashSet<TileKey>();
               _usedTiles[tilesetName] = set;
           }
           set.UnionWith(tiles);
       }
   }
   ```

2. **Builder pattern for complex objects:**
   ```csharp
   public class TilesetImageBuilder {
       private int _cols = 16;
       private int _tileSize = 8;
       private Image<Rgba32>? _sourceImage;
       private Dictionary<TileKey, int>? _tileMapping;

       public TilesetImageBuilder WithColumns(int cols) {
           _cols = cols;
           return this;
       }

       public TilesetImageBuilder WithTileSize(int size) {
           _tileSize = size;
           return this;
       }

       public TilesetImageBuilder FromSource(Image<Rgba32> source) {
           _sourceImage = source;
           return this;
       }

       public (Image<Rgba32> Image, Dictionary<TileKey, int> Mapping) Build() {
           // Validation and construction
       }
   }
   ```

3. **LINQ for tile processing:**
   ```csharp
   public Dictionary<TileKey, int> BuildTileMapping(
       HashSet<TileKey> usedTiles)
   {
       return usedTiles
           .OrderBy(t => t.TileId)
           .ThenBy(t => t.PaletteIndex)
           .Select((tile, index) => (tile, gid: index + 1))
           .ToDictionary(x => x.tile, x => x.gid);
   }
   ```

4. **Palette handling with records:**
   ```csharp
   public record PaletteConfiguration(
       IReadOnlyList<Color> PrimaryPalettes,
       IReadOnlyList<Color> SecondaryPalettes)
   {
       public Color GetColor(int paletteIndex, int colorIndex) {
           // GBA palette logic: 0-5 from primary, 6-12 from secondary
           var palette = paletteIndex < 6
               ? PrimaryPalettes
               : SecondaryPalettes;
           return palette[colorIndex];
       }
   }
   ```

---

### 6. id_transformer.py (29KB / ~829 lines) - ID NORMALIZATION

**Purpose:** Transforms Pokemon Emerald IDs to unified format (`base:type:category/name`).

#### Current Pain Points:
1. **String concatenation everywhere** - Manual ID building
2. **Regex normalization** - Complex `_normalize()` method
3. **Hardcoded prefixes** - Lots of string literal matching
4. **No validation** - IDs can be malformed
5. **Static methods only** - No state, just utility functions

#### Current Pattern:

```python
@classmethod
def map_id(cls, pokeemerald_map_id: str, region: Optional[str] = None) -> str:
    name = pokeemerald_map_id
    if name.startswith("MAP_"):
        name = name[4:]
    name = cls._normalize(name)
    category = region or cls.DEFAULT_REGION
    return cls.create_id(EntityType.MAP, category, name)
```

#### C# Improvements:

1. **Strongly typed ID system with source generators:**
   ```csharp
   [GameId("map")]
   public readonly record struct MapId(string Namespace, string Region, string Name) {
       public override string ToString() => $"{Namespace}:map:{Region}/{Name}";

       public static MapId Parse(string id) {
           var match = IdPattern.Match(id);
           if (!match.Success)
               throw new FormatException($"Invalid MapId: {id}");
           return new MapId(
               match.Groups["ns"].Value,
               match.Groups["region"].Value,
               match.Groups["name"].Value
           );
       }
   }

   // Source generator creates:
   // - Implicit conversion from string
   // - JSON converter
   // - EF Core value converter
   ```

2. **Use compiled regex:**
   ```csharp
   public static partial class IdPatterns {
       [GeneratedRegex(@"^(?<ns>[a-z0-9_]+):(?<type>[a-z]+):(?<path>.+)$")]
       private static partial Regex IdPatternRegex();

       public static Regex IdPattern => IdPatternRegex();
   }
   ```

3. **Span-based string manipulation:**
   ```csharp
   public static string Normalize(ReadOnlySpan<char> input) {
       Span<char> output = stackalloc char[input.Length];
       int written = 0;

       foreach (var c in input) {
           if (char.IsLetterOrDigit(c)) {
               output[written++] = char.ToLowerInvariant(c);
           }
           else if (c is ' ' or '-' or '_') {
               if (written > 0 && output[written - 1] != '_')
                   output[written++] = '_';
           }
       }

       return new string(output[..written]).Trim('_');
   }
   ```

4. **Builder with validation:**
   ```csharp
   public class IdBuilder {
       private string? _namespace;
       private string? _type;
       private string? _category;
       private string? _name;

       public IdBuilder WithNamespace(string ns) {
           ValidateComponent(ns);
           _namespace = ns;
           return this;
       }

       public string Build() {
           ValidateComplete();
           return $"{_namespace}:{_type}:{_category}/{_name}";
       }

       private void ValidateComponent(string component) {
           if (!IdPatterns.ComponentPattern.IsMatch(component))
               throw new ArgumentException($"Invalid ID component: {component}");
       }
   }
   ```

---

## Cross-Cutting Concerns

### 1. Configuration Management

**Current:** Hardcoded paths, constants in Python files
**Target:** appsettings.json with IOptions pattern

```csharp
public class PoryconOptions {
    public string InputDir { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    public bool TiledMode { get; set; }
    public int TileSize { get; set; } = 8;
    public int MetatileSize { get; set; } = 16;
    public int MaxParallelTasks { get; set; } = Environment.ProcessorCount;
}

// appsettings.json
{
    "Porycon": {
        "InputDir": "/path/to/pokeemerald",
        "OutputDir": "/path/to/output",
        "TiledMode": false,
        "MaxParallelTasks": 8
    }
}
```

### 2. Logging

**Current:** Custom logger with print statements
**Target:** ILogger<T> with structured logging

```csharp
_logger.LogInformation(
    "Converting map {MapId} in region {Region} with dimensions {Width}x{Height}",
    mapId, region, width, height);

_logger.LogWarning(
    "Skipped {Count} out-of-bounds tiles for tileset {Tileset}",
    skippedCount, tilesetName);
```

### 3. Error Handling

**Current:** Try/except with warning logs, silent failures
**Target:** Result pattern with proper exceptions

```csharp
public record Result<T> {
    public bool IsSuccess { get; init; }
    public T? Value { get; init; }
    public Error? Error { get; init; }

    public static Result<T> Success(T value) => new() { IsSuccess = true, Value = value };
    public static Result<T> Failure(Error error) => new() { Error = error };
}

public async Task<Result<ConversionOutput>> ConvertMapAsync(
    string mapId,
    CancellationToken ct)
{
    try {
        var output = await PerformConversionAsync(mapId, ct);
        return Result<ConversionOutput>.Success(output);
    }
    catch (FileNotFoundException ex) {
        return Result<ConversionOutput>.Failure(
            new Error("MapNotFound", $"Map file not found: {ex.FileName}"));
    }
    catch (InvalidDataException ex) {
        return Result<ConversionOutput>.Failure(
            new Error("InvalidData", ex.Message));
    }
}
```

### 4. Testing Strategy

**Current:** No tests
**Target:** Unit + Integration tests

```csharp
public class MapConverterTests {
    [Fact]
    public async Task ConvertMap_WithValidData_ReturnsSuccess() {
        // Arrange
        var converter = CreateConverter();
        var mapId = "MAP_LITTLEROOT_TOWN";

        // Act
        var result = await converter.ConvertMapAsync(mapId, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Layers.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("general", 512)]
    [InlineData("rustboro", 640)]
    public void LoadTileset_ReturnsCorrectTileCount(string tileset, int expected) {
        // Arrange
        var builder = new TilesetBuilder(_inputDir);

        // Act
        var image = builder.LoadTilesetGraphics(tileset);

        // Assert
        var tileCount = (image.Width / 8) * (image.Height / 8);
        tileCount.Should().Be(expected);
    }
}
```

---

## Performance Optimization Opportunities

### 1. Parallel Processing

**Maps are independent** - Convert multiple maps in parallel:
```csharp
public async Task<IEnumerable<Result<ConversionOutput>>> ConvertAllMapsAsync(
    IEnumerable<string> mapIds,
    IProgress<ConversionProgress>? progress = null,
    CancellationToken ct = default)
{
    var options = new ParallelOptions {
        MaxDegreeOfParallelism = _options.MaxParallelTasks,
        CancellationToken = ct
    };

    var results = new ConcurrentBag<Result<ConversionOutput>>();

    await Parallel.ForEachAsync(mapIds, options, async (mapId, ct) => {
        var result = await ConvertMapAsync(mapId, ct);
        results.Add(result);
        progress?.Report(new ConversionProgress(results.Count, mapIds.Count()));
    });

    return results;
}
```

### 2. Memory Pooling

**Reduce allocations** for frequently created objects:
```csharp
public class MetatileProcessor {
    private readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;
    private readonly ArrayPool<int> _intPool = ArrayPool<int>.Shared;

    public void ProcessMetatiles(ReadOnlySpan<ushort> mapData) {
        var buffer = _intPool.Rent(mapData.Length);
        try {
            // Process using rented buffer
        }
        finally {
            _intPool.Return(buffer);
        }
    }
}
```

### 3. Span<T> for Binary Data

**Avoid allocations** when reading binary files:
```csharp
public ReadOnlySpan<ushort> ReadMapBin(string path, int width, int height) {
    var expectedSize = width * height * sizeof(ushort);
    using var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
    using var accessor = mmf.CreateViewAccessor(0, expectedSize);

    var buffer = new ushort[width * height];
    accessor.ReadArray(0, buffer, 0, buffer.Length);

    return buffer;
}
```

### 4. Incremental Conversion

**Track changes** and only convert modified maps:
```csharp
public class IncrementalConverter {
    private readonly IChangeTracker _changeTracker;

    public async Task<ConversionResults> ConvertChangedMapsAsync(
        CancellationToken ct)
    {
        var changedMaps = await _changeTracker.GetChangedMapsAsync(ct);
        return await ConvertMapsAsync(changedMaps, ct);
    }
}

public interface IChangeTracker {
    Task<IEnumerable<string>> GetChangedMapsAsync(CancellationToken ct);
    Task MarkAsProcessedAsync(string mapId, DateTime timestamp, CancellationToken ct);
}
```

---

## Architecture Recommendations

### Layered Architecture

```
┌─────────────────────────────────────┐
│         CLI / API Layer             │  (Commands, Controllers)
├─────────────────────────────────────┤
│       Application Services          │  (MapConversionService, AudioConversionService)
├─────────────────────────────────────┤
│         Domain Services             │  (TilesetBuilder, MetatileRenderer, IdTransformer)
├─────────────────────────────────────┤
│         Infrastructure              │  (FileSystem, ExternalTools, BinaryParsers)
├─────────────────────────────────────┤
│            Data Models              │  (Entities, DTOs, Configuration)
└─────────────────────────────────────┘
```

### Project Structure

```
Porycon2.sln
├── Porycon2.CLI/                    # Command-line interface
│   ├── Commands/
│   │   ├── ConvertMapCommand.cs
│   │   └── ConvertAllCommand.cs
│   └── Program.cs
├── Porycon2.Core/                   # Domain logic
│   ├── Entities/
│   │   ├── MapId.cs
│   │   ├── MetatileKey.cs
│   │   └── TileKey.cs
│   ├── Services/
│   │   ├── IMapConverter.cs
│   │   ├── ITilesetBuilder.cs
│   │   └── IIdTransformer.cs
│   └── Models/
│       ├── TiledMap.cs
│       └── MapEntity.cs
├── Porycon2.Infrastructure/         # External dependencies
│   ├── BinaryParsers/
│   │   ├── MapBinReader.cs
│   │   └── MetatileBinReader.cs
│   ├── ExternalTools/
│   │   ├── Mid2AgbExecutor.cs
│   │   └── GbamusriperExecutor.cs
│   ├── FileSystem/
│   │   ├── TilesetPathResolver.cs
│   │   └── MapFileLocator.cs
│   └── ImageProcessing/
│       ├── ImageTileExtractor.cs
│       └── PaletteApplicator.cs
├── Porycon2.Application/            # Use cases
│   ├── MapConversion/
│   │   ├── MapConversionService.cs
│   │   ├── ConvertMapCommand.cs
│   │   └── ConvertMapHandler.cs
│   ├── AudioConversion/
│   └── TilesetGeneration/
└── Porycon2.Tests/
    ├── Unit/
    ├── Integration/
    └── TestData/
```

### Dependency Injection Setup

```csharp
public static class ServiceCollectionExtensions {
    public static IServiceCollection AddPorycon(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<PoryconOptions>(configuration.GetSection("Porycon"));
        services.Configure<AudioConverterOptions>(configuration.GetSection("AudioConverter"));

        // Core services
        services.AddSingleton<IIdTransformer, IdTransformer>();
        services.AddScoped<IMapConverter, MapConverter>();
        services.AddScoped<ITilesetBuilder, TilesetBuilder>();
        services.AddScoped<IMetatileRenderer, MetatileRenderer>();
        services.AddScoped<IAnimationScanner, AnimationScanner>();

        // Infrastructure
        services.AddScoped<IMapReader, MapReader>();
        services.AddScoped<IBinaryParser, BinaryParser>();
        services.AddScoped<ITilesetPathResolver, TilesetPathResolver>();
        services.AddScoped<IExternalToolExecutor, ExternalToolExecutor>();

        // Application services
        services.AddScoped<MapConversionService>();
        services.AddScoped<AudioConversionService>();

        // Memory cache for animation data
        services.AddMemoryCache();

        // Logging
        services.AddLogging(builder => builder.AddConsole());

        return services;
    }
}
```

---

## Migration Strategy

### Phase 1: Foundation (Week 1-2)
- [ ] Set up project structure
- [ ] Create core entity models (MapId, TileKey, etc.)
- [ ] Implement IdTransformer (most isolated module)
- [ ] Implement binary parsers (MapBinReader, MetatileBinReader)
- [ ] Set up unit test framework

### Phase 2: Core Services (Week 3-4)
- [ ] TilesetBuilder with palette handling
- [ ] MetatileRenderer with proper image types
- [ ] AnimationScanner with JSON config
- [ ] MapReader with async file I/O

### Phase 3: Converters (Week 5-6)
- [ ] MapConverter with parallel processing
- [ ] DefinitionConverter with AutoMapper
- [ ] AudioConverter with retry logic
- [ ] Integration tests for full pipeline

### Phase 4: Polish (Week 7-8)
- [ ] CLI with progress reporting
- [ ] Error handling and validation
- [ ] Performance optimization
- [ ] Documentation and examples

---

## Conclusion

### Benefits of C# Rewrite:

1. **Type Safety:** Eliminate dict/tuple hell with proper classes
2. **Performance:** 2-5x faster with parallel processing and Span<T>
3. **Maintainability:** Clear separation of concerns, DI, and SOLID principles
4. **Testability:** Proper interfaces and mocking
5. **Modern Tooling:** Visual Studio debugging, IntelliSense, refactoring
6. **Memory Efficiency:** Pooling, spans, and proper disposal
7. **Error Handling:** Result pattern instead of silent failures
8. **Async/Await:** Non-blocking I/O for better throughput

### Estimated Effort:
- **Lines of Code:** ~15,000-20,000 lines C# (vs ~25,000 Python)
- **Time:** 6-8 weeks for full rewrite with tests
- **Team:** 1-2 developers
- **Risk:** Low - Python version works, C# is port with improvements

### Next Steps:
1. Review this analysis
2. Approve architecture and project structure
3. Start with Phase 1 (Foundation)
4. Iterate with frequent demos of working features
