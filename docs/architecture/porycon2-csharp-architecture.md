# Porycon2 C# Architecture Design

## Executive Summary

This document defines a comprehensive architecture for rewriting the porycon2 Python tool in C#. The design emphasizes modularity, maintainability, testability, and performance through async/parallel operations.

## Architecture Decision Records (ADRs)

### ADR-001: Command-Line Framework
**Decision**: Use Spectre.Console.Cli for CLI interface
**Rationale**:
- Type-safe command definitions
- Built-in dependency injection support
- Excellent progress visualization
- Validation framework integration
- Better than System.CommandLine for complex scenarios

### ADR-002: Dependency Injection
**Decision**: Microsoft.Extensions.DependencyInjection
**Rationale**:
- Industry standard for .NET
- Seamless integration with logging, configuration
- Testability through interface abstractions
- Lifetime management (Singleton, Scoped, Transient)

### ADR-003: Asynchronous Processing
**Decision**: Async/await throughout with TPL DataFlow for pipelines
**Rationale**:
- File I/O is inherently async
- Parallel processing of multiple maps/tilesets
- TPL DataFlow provides robust pipeline patterns
- Better resource utilization

### ADR-004: Configuration Management
**Decision**: Microsoft.Extensions.Configuration with appsettings.json + environment variables
**Rationale**:
- Hierarchical configuration
- Environment-specific overrides
- Strong typing through IOptions pattern
- Easy testing with in-memory providers

---

## 1. Project Structure

### Solution Organization

```
Porycon2.sln
├── src/
│   ├── Porycon2.Cli/                    # Entry point, commands, DI setup
│   ├── Porycon2.Core/                   # Core business logic, services
│   ├── Porycon2.Domain/                 # Domain models, interfaces
│   ├── Porycon2.Infrastructure/         # File I/O, external dependencies
│   └── Porycon2.Converters/             # Format-specific converters
├── tests/
│   ├── Porycon2.Core.Tests/
│   ├── Porycon2.Converters.Tests/
│   └── Porycon2.Integration.Tests/
└── docs/
    └── architecture/
```

### Namespace Hierarchy

```
Porycon2.Cli
├── Commands/                  # Spectre.Console.Cli commands
├── Settings/                  # Command settings classes
├── Infrastructure/            # DI configuration, logging setup
└── Program.cs

Porycon2.Domain
├── Models/                    # Domain entities
│   ├── Maps/
│   ├── Tilesets/
│   ├── Audio/
│   └── Common/
├── Interfaces/                # Abstractions
│   ├── Converters/
│   ├── Services/
│   └── Repositories/
├── Enums/                     # Domain enumerations
└── Exceptions/                # Domain-specific exceptions

Porycon2.Core
├── Services/                  # Business logic services
│   ├── Conversion/
│   ├── Validation/
│   └── Processing/
├── Pipelines/                 # TPL DataFlow pipelines
└── Extensions/                # Helper extensions

Porycon2.Infrastructure
├── IO/                        # File system operations
│   ├── Readers/
│   └── Writers/
├── Serialization/             # JSON/Binary serializers
└── Caching/                   # Performance optimizations

Porycon2.Converters
├── Maps/                      # Map conversion implementations
├── Tilesets/                  # Tileset conversion implementations
├── Audio/                     # Audio conversion implementations
└── Metadata/                  # Popup/metadata extraction
```

---

## 2. Core Abstractions & Interfaces

### 2.1 Conversion Pipeline Interfaces

```csharp
namespace Porycon2.Domain.Interfaces.Converters
{
    /// <summary>
    /// Base converter interface for all conversion operations
    /// </summary>
    public interface IConverter<TInput, TOutput>
    {
        /// <summary>
        /// Converts input data to output format
        /// </summary>
        Task<TOutput> ConvertAsync(
            TInput input,
            ConversionContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates if input can be converted
        /// </summary>
        Task<ValidationResult> ValidateAsync(
            TInput input,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Map converter interface
    /// </summary>
    public interface IMapConverter : IConverter<MapSource, MapDefinition>
    {
        /// <summary>
        /// Gets supported map format
        /// </summary>
        MapFormat SupportedFormat { get; }

        /// <summary>
        /// Batch converts multiple maps
        /// </summary>
        IAsyncEnumerable<ConversionResult<MapDefinition>> ConvertBatchAsync(
            IEnumerable<MapSource> sources,
            ConversionContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Tileset builder interface
    /// </summary>
    public interface ITilesetBuilder
    {
        /// <summary>
        /// Builds tileset from map data
        /// </summary>
        Task<Tileset> BuildAsync(
            MapDefinition map,
            TilesetBuildOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Scans for animations in tileset
        /// </summary>
        Task<IReadOnlyList<TileAnimation>> ScanAnimationsAsync(
            Tileset tileset,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Optimizes tileset by removing duplicates
        /// </summary>
        Task<Tileset> OptimizeAsync(
            Tileset tileset,
            OptimizationOptions options,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// World/Region builder interface
    /// </summary>
    public interface IWorldBuilder
    {
        /// <summary>
        /// Builds world structure from map collection
        /// </summary>
        Task<WorldDefinition> BuildWorldAsync(
            IEnumerable<MapDefinition> maps,
            WorldBuildOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Organizes maps into regional hierarchy
        /// </summary>
        Task<IReadOnlyList<Region>> OrganizeMapsAsync(
            IEnumerable<MapDefinition> maps,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Audio converter interface
    /// </summary>
    public interface IAudioConverter
    {
        /// <summary>
        /// Converts audio file to target format
        /// </summary>
        Task<AudioFile> ConvertAsync(
            string sourcePath,
            AudioFormat targetFormat,
            AudioConversionOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Batch converts audio files
        /// </summary>
        IAsyncEnumerable<ConversionResult<AudioFile>> ConvertBatchAsync(
            IEnumerable<string> sourcePaths,
            AudioFormat targetFormat,
            AudioConversionOptions options,
            CancellationToken cancellationToken = default);
    }
}
```

### 2.2 Repository Interfaces

```csharp
namespace Porycon2.Domain.Interfaces.Repositories
{
    /// <summary>
    /// Generic repository for data access
    /// </summary>
    public interface IRepository<TEntity, TKey> where TEntity : class
    {
        Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
        Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);
        Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);
        Task DeleteAsync(TKey id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Map repository for persisting map definitions
    /// </summary>
    public interface IMapRepository : IRepository<MapDefinition, string>
    {
        Task<IEnumerable<MapDefinition>> GetByRegionAsync(
            string regionName,
            CancellationToken cancellationToken = default);

        Task<IEnumerable<MapDefinition>> SearchAsync(
            MapSearchCriteria criteria,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Tileset repository
    /// </summary>
    public interface ITilesetRepository : IRepository<Tileset, string>
    {
        Task<bool> ExistsAsync(
            string name,
            CancellationToken cancellationToken = default);

        Task<Tileset?> GetByHashAsync(
            string hash,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// File system repository for raw file operations
    /// </summary>
    public interface IFileRepository
    {
        Task<byte[]> ReadBytesAsync(
            string path,
            CancellationToken cancellationToken = default);

        Task WriteBytesAsync(
            string path,
            byte[] data,
            CancellationToken cancellationToken = default);

        Task<string> ReadTextAsync(
            string path,
            CancellationToken cancellationToken = default);

        Task WriteTextAsync(
            string path,
            string content,
            CancellationToken cancellationToken = default);

        IAsyncEnumerable<string> EnumerateFilesAsync(
            string directory,
            string searchPattern,
            CancellationToken cancellationToken = default);
    }
}
```

### 2.3 Service Interfaces

```csharp
namespace Porycon2.Domain.Interfaces.Services
{
    /// <summary>
    /// Map conversion orchestration service
    /// </summary>
    public interface IMapConversionService
    {
        /// <summary>
        /// Converts a single map from source to target format
        /// </summary>
        Task<ConversionResult<MapDefinition>> ConvertMapAsync(
            string sourcePath,
            string outputPath,
            MapConversionOptions options,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Batch converts multiple maps in parallel
        /// </summary>
        Task<BatchConversionResult> ConvertMapsAsync(
            IEnumerable<string> sourcePaths,
            string outputDirectory,
            MapConversionOptions options,
            IProgress<BatchConversionProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Tileset building and optimization service
    /// </summary>
    public interface ITilesetService
    {
        /// <summary>
        /// Builds tilesets from map definitions
        /// </summary>
        Task<IReadOnlyList<Tileset>> BuildTilesetsAsync(
            IEnumerable<MapDefinition> maps,
            TilesetBuildOptions options,
            IProgress<TilesetProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Scans and extracts animations from tilesets
        /// </summary>
        Task<AnimationScanResult> ScanAnimationsAsync(
            IEnumerable<Tileset> tilesets,
            AnimationScanOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Optimizes tileset by removing duplicate metatiles
        /// </summary>
        Task<OptimizationResult> OptimizeTilesetAsync(
            Tileset tileset,
            OptimizationOptions options,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Audio conversion service
    /// </summary>
    public interface IAudioConversionService
    {
        /// <summary>
        /// Converts audio files to target format
        /// </summary>
        Task<ConversionResult<AudioFile>> ConvertAudioAsync(
            string sourcePath,
            string outputPath,
            AudioFormat targetFormat,
            AudioConversionOptions options,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Batch converts audio files
        /// </summary>
        Task<BatchConversionResult> ConvertAudioBatchAsync(
            IEnumerable<string> sourcePaths,
            string outputDirectory,
            AudioFormat targetFormat,
            AudioConversionOptions options,
            IProgress<BatchConversionProgress>? progress = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Metadata extraction service
    /// </summary>
    public interface IMetadataExtractionService
    {
        /// <summary>
        /// Extracts popup metadata from maps
        /// </summary>
        Task<IReadOnlyList<PopupMetadata>> ExtractPopupsAsync(
            IEnumerable<MapDefinition> maps,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Extracts warp point metadata
        /// </summary>
        Task<IReadOnlyList<WarpMetadata>> ExtractWarpsAsync(
            IEnumerable<MapDefinition> maps,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Validation service for data integrity
    /// </summary>
    public interface IValidationService
    {
        /// <summary>
        /// Validates map definition
        /// </summary>
        Task<ValidationResult> ValidateMapAsync(
            MapDefinition map,
            ValidationOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates tileset integrity
        /// </summary>
        Task<ValidationResult> ValidateTilesetAsync(
            Tileset tileset,
            ValidationOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates world/region structure
        /// </summary>
        Task<ValidationResult> ValidateWorldAsync(
            WorldDefinition world,
            ValidationOptions options,
            CancellationToken cancellationToken = default);
    }
}
```

---

## 3. Domain Models

### 3.1 Core Map Models

```csharp
namespace Porycon2.Domain.Models.Maps
{
    /// <summary>
    /// Represents a map definition in the target format
    /// </summary>
    public sealed class MapDefinition
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required MapDimensions Dimensions { get; init; }
        public required string TilesetName { get; init; }
        public required MapRegion Region { get; init; }
        public required MapLayout Layout { get; init; }

        public IReadOnlyList<MapConnection> Connections { get; init; } = Array.Empty<MapConnection>();
        public IReadOnlyList<MapObject> Objects { get; init; } = Array.Empty<MapObject>();
        public IReadOnlyList<WarpEvent> Warps { get; init; } = Array.Empty<WarpEvent>();
        public IReadOnlyList<TriggerEvent> Triggers { get; init; } = Array.Empty<TriggerEvent>();
        public IReadOnlyList<SignEvent> Signs { get; init; } = Array.Empty<SignEvent>();

        public MapMetadata Metadata { get; init; } = MapMetadata.Default;
        public string? Music { get; init; }
        public WeatherType Weather { get; init; } = WeatherType.None;
        public MapType Type { get; init; } = MapType.Normal;
        public bool ShowLocationName { get; init; } = true;
        public BattleScene BattleScene { get; init; } = BattleScene.Default;
    }

    public sealed record MapDimensions(int Width, int Height)
    {
        public int TileCount => Width * Height;
    }

    public sealed class MapLayout
    {
        public required string Id { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required ushort[] PrimaryTiles { get; init; }
        public required ushort[] SecondaryTiles { get; init; }
        public required byte[] CollisionData { get; init; }

        public BorderLayout? Border { get; init; }
    }

    public sealed class BorderLayout
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required ushort[] Tiles { get; init; }
    }

    public sealed record MapRegion(string Name, string Group)
    {
        public static MapRegion Parse(string combined)
        {
            var parts = combined.Split('/', 2);
            return new MapRegion(parts[^1], parts.Length > 1 ? parts[0] : "Default");
        }
    }

    public sealed class MapConnection
    {
        public required ConnectionDirection Direction { get; init; }
        public required string TargetMapId { get; init; }
        public required int Offset { get; init; }
    }

    public sealed class MapObject
    {
        public required int Id { get; init; }
        public required string GraphicsId { get; init; }
        public required Position Position { get; init; }
        public required int ElevationHeight { get; init; }
        public required MovementType Movement { get; init; }
        public required Rectangle MovementRange { get; init; }

        public string? TrainerType { get; init; }
        public int? TrainerSightRange { get; init; }
        public string? Script { get; init; }
        public FacingDirection FacingDirection { get; init; } = FacingDirection.Down;
    }

    public sealed record Position(int X, int Y);
    public sealed record Rectangle(int X, int Y, int Width, int Height);

    public sealed class WarpEvent
    {
        public required Position Position { get; init; }
        public required int ElevationHeight { get; init; }
        public required string TargetMapId { get; init; }
        public required int TargetWarpId { get; init; }
    }

    public sealed class TriggerEvent
    {
        public required Position Position { get; init; }
        public required int ElevationHeight { get; init; }
        public required string Variable { get; init; }
        public required int VariableValue { get; init; }
        public required string Script { get; init; }
    }

    public sealed class SignEvent
    {
        public required Position Position { get; init; }
        public required int ElevationHeight { get; init; }
        public required SignType Type { get; init; }
        public string? Script { get; init; }
        public string? HiddenItemId { get; init; }
        public int? HiddenItemQuantity { get; init; }
    }

    public sealed class MapMetadata
    {
        public static readonly MapMetadata Default = new();

        public string? LocationName { get; init; }
        public string? Description { get; init; }
        public Dictionary<string, string> CustomProperties { get; init; } = new();
    }
}
```

### 3.2 Tileset Models

```csharp
namespace Porycon2.Domain.Models.Tilesets
{
    /// <summary>
    /// Represents a complete tileset definition
    /// </summary>
    public sealed class Tileset
    {
        public required string Name { get; init; }
        public required string Id { get; init; }
        public required bool IsPrimary { get; init; }

        public required TilesetGraphics Graphics { get; init; }
        public required IReadOnlyList<Metatile> Metatiles { get; init; }
        public required IReadOnlyList<TileAnimation> Animations { get; init; }
        public required PaletteSet Palettes { get; init; }

        public TilesetMetadata Metadata { get; init; } = TilesetMetadata.Default;

        public int MetatileCount => Metatiles.Count;
        public string Hash => ComputeHash();

        private string ComputeHash()
        {
            // SHA256 hash of metatile data for deduplication
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                string.Join(",", Metatiles.Select(m => m.GetHashCode())));
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }
    }

    public sealed class TilesetGraphics
    {
        public required byte[] TileData { get; init; }
        public required int TileCount { get; init; }
        public required GraphicsFormat Format { get; init; }
        public required int BitsPerPixel { get; init; }
    }

    public sealed class Metatile
    {
        public required int Id { get; init; }
        public required MetatileTile[] Tiles { get; init; } // 8 tiles (2x4 grid)
        public required MetatileBehavior Behavior { get; init; }
        public required TerrainType Terrain { get; init; }
        public required EncounterType Encounter { get; init; }
        public required MetatileAttributes Attributes { get; init; }

        public bool IsAnimated => Tiles.Any(t => t.IsAnimated);
    }

    public sealed class MetatileTile
    {
        public required ushort TileId { get; init; }
        public required int PaletteId { get; init; }
        public required bool FlipX { get; init; }
        public required bool FlipY { get; init; }
        public required bool IsAnimated { get; init; }
    }

    public sealed record MetatileBehavior(byte Value)
    {
        public bool IsImpassable => (Value & 0x01) != 0;
        public bool IsWater => (Value & 0x10) != 0;
        public bool IsSurfable => (Value & 0x20) != 0;
    }

    public sealed class MetatileAttributes
    {
        public required int LayerType { get; init; }
        public required int ElevationHeight { get; init; }
        public bool IsTransparent { get; init; }
        public bool CausesReflection { get; init; }
    }

    public sealed class TileAnimation
    {
        public required int Id { get; init; }
        public required IReadOnlyList<AnimationFrame> Frames { get; init; }
        public required int FrameDelay { get; init; }
        public required AnimationType Type { get; init; }

        public int TotalDuration => Frames.Count * FrameDelay;
    }

    public sealed class AnimationFrame
    {
        public required int FrameIndex { get; init; }
        public required ushort[] TileData { get; init; }
        public int Duration { get; init; }
    }

    public sealed class PaletteSet
    {
        public required IReadOnlyList<Palette> Palettes { get; init; }
        public required int PaletteCount { get; init; }

        public const int ColorsPerPalette = 16;
        public const int MaxPalettes = 16;
    }

    public sealed class Palette
    {
        public required int Id { get; init; }
        public required Color[] Colors { get; init; } // 16 colors

        public Palette()
        {
            Colors = new Color[PaletteSet.ColorsPerPalette];
        }
    }

    public readonly record struct Color(byte R, byte G, byte B)
    {
        public static Color FromRgb555(ushort rgb555)
        {
            byte r = (byte)((rgb555 & 0x1F) << 3);
            byte g = (byte)(((rgb555 >> 5) & 0x1F) << 3);
            byte b = (byte)(((rgb555 >> 10) & 0x1F) << 3);
            return new Color(r, g, b);
        }

        public ushort ToRgb555()
        {
            return (ushort)(
                (R >> 3) |
                ((G >> 3) << 5) |
                ((B >> 3) << 10)
            );
        }
    }

    public sealed class TilesetMetadata
    {
        public static readonly TilesetMetadata Default = new();

        public string? SourceGame { get; init; }
        public string? Author { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public Dictionary<string, string> CustomProperties { get; init; } = new();
    }
}
```

### 3.3 Audio Models

```csharp
namespace Porycon2.Domain.Models.Audio
{
    public sealed class AudioFile
    {
        public required string FileName { get; init; }
        public required AudioFormat Format { get; init; }
        public required byte[] Data { get; init; }
        public required AudioMetadata Metadata { get; init; }

        public long SizeInBytes => Data.Length;
    }

    public sealed class AudioMetadata
    {
        public required int SampleRate { get; init; }
        public required int Channels { get; init; }
        public required int BitsPerSample { get; init; }
        public required TimeSpan Duration { get; init; }

        public string? Title { get; init; }
        public string? Artist { get; init; }
        public bool IsLooping { get; init; }
        public int? LoopStart { get; init; }
        public int? LoopEnd { get; init; }
    }
}
```

### 3.4 World/Region Models

```csharp
namespace Porycon2.Domain.Models.World
{
    public sealed class WorldDefinition
    {
        public required string Name { get; init; }
        public required IReadOnlyList<Region> Regions { get; init; }
        public required WorldMetadata Metadata { get; init; }

        public int TotalMapCount => Regions.Sum(r => r.Maps.Count);
    }

    public sealed class Region
    {
        public required string Name { get; init; }
        public required string Group { get; init; }
        public required IReadOnlyList<MapDefinition> Maps { get; init; }
        public required RegionMetadata Metadata { get; init; }
    }

    public sealed class WorldMetadata
    {
        public string? Version { get; init; }
        public string? SourceGame { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public Dictionary<string, string> CustomProperties { get; init; } = new();
    }

    public sealed class RegionMetadata
    {
        public string? DisplayName { get; init; }
        public string? Description { get; init; }
        public Dictionary<string, string> CustomProperties { get; init; } = new();
    }
}
```

---

## 4. Command Structure (Spectre.Console.Cli)

### 4.1 Command Architecture

```csharp
namespace Porycon2.Cli.Commands
{
    /// <summary>
    /// Root command - displays help and version
    /// </summary>
    public sealed class RootCommand : Command<RootCommand.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandOption("--version")]
            [Description("Display version information")]
            public bool ShowVersion { get; init; }
        }

        public override int Execute(CommandContext context, Settings settings)
        {
            if (settings.ShowVersion)
            {
                AnsiConsole.WriteLine($"Porycon2 v{GetVersion()}");
                return 0;
            }

            // Display help
            AnsiConsole.Write(new FigletText("Porycon2").Color(Color.Cyan1));
            AnsiConsole.MarkupLine("[cyan]Pokemon map conversion tool[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("Use [yellow]--help[/] to see available commands");

            return 0;
        }
    }

    /// <summary>
    /// Convert maps command
    /// </summary>
    public sealed class ConvertCommand : AsyncCommand<ConvertCommand.Settings>
    {
        private readonly IMapConversionService _conversionService;
        private readonly ILogger<ConvertCommand> _logger;

        public ConvertCommand(
            IMapConversionService conversionService,
            ILogger<ConvertCommand> logger)
        {
            _conversionService = conversionService;
            _logger = logger;
        }

        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<input>")]
            [Description("Input file or directory")]
            public required string Input { get; init; }

            [CommandArgument(1, "<output>")]
            [Description("Output directory")]
            public required string Output { get; init; }

            [CommandOption("-f|--format <FORMAT>")]
            [Description("Output format (json, binary)")]
            [DefaultValue("json")]
            public string Format { get; init; } = "json";

            [CommandOption("--parallel <COUNT>")]
            [Description("Maximum parallel conversions")]
            [DefaultValue(4)]
            public int MaxParallelism { get; init; } = 4;

            [CommandOption("--recursive")]
            [Description("Process subdirectories recursively")]
            public bool Recursive { get; init; }

            [CommandOption("--overwrite")]
            [Description("Overwrite existing files")]
            public bool Overwrite { get; init; }

            [CommandOption("--validate")]
            [Description("Validate output after conversion")]
            public bool Validate { get; init; }

            public override ValidationResult Validate()
            {
                if (!Directory.Exists(Input) && !File.Exists(Input))
                    return ValidationResult.Error($"Input path not found: {Input}");

                if (MaxParallelism < 1 || MaxParallelism > 16)
                    return ValidationResult.Error("Parallelism must be between 1 and 16");

                return ValidationResult.Success();
            }
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            Settings settings)
        {
            try
            {
                await AnsiConsole.Status()
                    .StartAsync("Converting maps...", async ctx =>
                    {
                        var options = new MapConversionOptions
                        {
                            OutputFormat = Enum.Parse<OutputFormat>(settings.Format, true),
                            MaxParallelism = settings.MaxParallelism,
                            Recursive = settings.Recursive,
                            OverwriteExisting = settings.Overwrite,
                            ValidateOutput = settings.Validate
                        };

                        var progress = new Progress<BatchConversionProgress>(p =>
                        {
                            ctx.Status($"Processing {p.CurrentFile} ({p.Processed}/{p.Total})");
                        });

                        var sourcePaths = GetSourcePaths(settings.Input, settings.Recursive);
                        var result = await _conversionService.ConvertMapsAsync(
                            sourcePaths,
                            settings.Output,
                            options,
                            progress);

                        DisplayResults(result);
                    });

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversion failed");
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                return 1;
            }
        }

        private static IEnumerable<string> GetSourcePaths(string input, bool recursive)
        {
            if (File.Exists(input))
                return new[] { input };

            var searchOption = recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            return Directory.EnumerateFiles(input, "*.map", searchOption);
        }

        private static void DisplayResults(BatchConversionResult result)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Metric")
                .AddColumn("Value");

            table.AddRow("Total Files", result.TotalFiles.ToString());
            table.AddRow("Successful", $"[green]{result.SuccessCount}[/]");
            table.AddRow("Failed", result.FailedCount > 0
                ? $"[red]{result.FailedCount}[/]"
                : "0");
            table.AddRow("Duration", result.Duration.ToString(@"mm\:ss\.fff"));
            table.AddRow("Files/Second",
                $"{result.TotalFiles / result.Duration.TotalSeconds:F2}");

            AnsiConsole.Write(table);

            if (result.Errors.Any())
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[red]Errors:[/]");
                foreach (var error in result.Errors)
                {
                    AnsiConsole.MarkupLine($"  [red]•[/] {error}");
                }
            }
        }
    }

    /// <summary>
    /// Build tilesets command
    /// </summary>
    public sealed class BuildTilesetsCommand : AsyncCommand<BuildTilesetsCommand.Settings>
    {
        private readonly ITilesetService _tilesetService;
        private readonly IMapRepository _mapRepository;
        private readonly ILogger<BuildTilesetsCommand> _logger;

        public BuildTilesetsCommand(
            ITilesetService tilesetService,
            IMapRepository mapRepository,
            ILogger<BuildTilesetsCommand> logger)
        {
            _tilesetService = tilesetService;
            _mapRepository = mapRepository;
            _logger = logger;
        }

        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<maps-directory>")]
            [Description("Directory containing map definitions")]
            public required string MapsDirectory { get; init; }

            [CommandArgument(1, "<output-directory>")]
            [Description("Output directory for tilesets")]
            public required string OutputDirectory { get; init; }

            [CommandOption("--scan-animations")]
            [Description("Scan and extract tile animations")]
            public bool ScanAnimations { get; init; }

            [CommandOption("--optimize")]
            [Description("Optimize tilesets by removing duplicates")]
            public bool Optimize { get; init; }

            [CommandOption("--primary-only")]
            [Description("Build only primary tilesets")]
            public bool PrimaryOnly { get; init; }

            public override ValidationResult Validate()
            {
                if (!Directory.Exists(MapsDirectory))
                    return ValidationResult.Error($"Maps directory not found: {MapsDirectory}");

                return ValidationResult.Success();
            }
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            Settings settings)
        {
            try
            {
                var maps = await _mapRepository.GetAllAsync();

                var options = new TilesetBuildOptions
                {
                    ScanAnimations = settings.ScanAnimations,
                    Optimize = settings.Optimize,
                    PrimaryOnly = settings.PrimaryOnly,
                    OutputDirectory = settings.OutputDirectory
                };

                var progress = new Progress<TilesetProgress>(p =>
                {
                    AnsiConsole.MarkupLine(
                        $"Building tileset [cyan]{p.CurrentTileset}[/] ({p.Processed}/{p.Total})");
                });

                var tilesets = await _tilesetService.BuildTilesetsAsync(
                    maps,
                    options,
                    progress);

                AnsiConsole.MarkupLine(
                    $"[green]Successfully built {tilesets.Count} tilesets[/]");

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tileset building failed");
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                return 1;
            }
        }
    }

    /// <summary>
    /// Extract audio command
    /// </summary>
    public sealed class ExtractAudioCommand : AsyncCommand<ExtractAudioCommand.Settings>
    {
        private readonly IAudioConversionService _audioService;
        private readonly ILogger<ExtractAudioCommand> _logger;

        public ExtractAudioCommand(
            IAudioConversionService audioService,
            ILogger<ExtractAudioCommand> logger)
        {
            _audioService = audioService;
            _logger = logger;
        }

        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<input>")]
            [Description("Input audio file or directory")]
            public required string Input { get; init; }

            [CommandArgument(1, "<output>")]
            [Description("Output directory")]
            public required string Output { get; init; }

            [CommandOption("-f|--format <FORMAT>")]
            [Description("Output format (wav, mp3, ogg)")]
            [DefaultValue("wav")]
            public string Format { get; init; } = "wav";

            [CommandOption("--sample-rate <RATE>")]
            [Description("Target sample rate")]
            [DefaultValue(44100)]
            public int SampleRate { get; init; } = 44100;

            [CommandOption("--preserve-loops")]
            [Description("Preserve loop points in output")]
            public bool PreserveLoops { get; init; }

            public override ValidationResult Validate()
            {
                if (!Directory.Exists(Input) && !File.Exists(Input))
                    return ValidationResult.Error($"Input path not found: {Input}");

                if (!Enum.TryParse<AudioFormat>(Format, true, out _))
                    return ValidationResult.Error($"Invalid format: {Format}");

                return ValidationResult.Success();
            }
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            Settings settings)
        {
            try
            {
                var targetFormat = Enum.Parse<AudioFormat>(settings.Format, true);
                var options = new AudioConversionOptions
                {
                    SampleRate = settings.SampleRate,
                    PreserveLoops = settings.PreserveLoops
                };

                var sourcePaths = File.Exists(settings.Input)
                    ? new[] { settings.Input }
                    : Directory.EnumerateFiles(settings.Input, "*.*", SearchOption.AllDirectories)
                        .Where(f => IsAudioFile(f));

                var progress = new Progress<BatchConversionProgress>(p =>
                {
                    AnsiConsole.MarkupLine(
                        $"Converting [cyan]{Path.GetFileName(p.CurrentFile)}[/] ({p.Processed}/{p.Total})");
                });

                var result = await _audioService.ConvertAudioBatchAsync(
                    sourcePaths,
                    settings.Output,
                    targetFormat,
                    options,
                    progress);

                AnsiConsole.MarkupLine(
                    $"[green]Successfully converted {result.SuccessCount} files[/]");

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio conversion failed");
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                return 1;
            }
        }

        private static bool IsAudioFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".wav" or ".mp3" or ".ogg" or ".flac" or ".mid";
        }
    }

    /// <summary>
    /// Extract metadata (popups, warps, etc.) command
    /// </summary>
    public sealed class ExtractMetadataCommand : AsyncCommand<ExtractMetadataCommand.Settings>
    {
        private readonly IMetadataExtractionService _metadataService;
        private readonly IMapRepository _mapRepository;
        private readonly ILogger<ExtractMetadataCommand> _logger;

        public ExtractMetadataCommand(
            IMetadataExtractionService metadataService,
            IMapRepository mapRepository,
            ILogger<ExtractMetadataCommand> logger)
        {
            _metadataService = metadataService;
            _mapRepository = mapRepository;
            _logger = logger;
        }

        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<maps-directory>")]
            [Description("Directory containing map definitions")]
            public required string MapsDirectory { get; init; }

            [CommandArgument(1, "<output-file>")]
            [Description("Output file path")]
            public required string OutputFile { get; init; }

            [CommandOption("--type <TYPE>")]
            [Description("Metadata type (popups, warps, all)")]
            [DefaultValue("all")]
            public string Type { get; init; } = "all";

            [CommandOption("--format <FORMAT>")]
            [Description("Output format (json, csv)")]
            [DefaultValue("json")]
            public string Format { get; init; } = "json";

            public override ValidationResult Validate()
            {
                if (!Directory.Exists(MapsDirectory))
                    return ValidationResult.Error($"Maps directory not found: {MapsDirectory}");

                if (Type.ToLowerInvariant() is not ("popups" or "warps" or "all"))
                    return ValidationResult.Error($"Invalid type: {Type}");

                return ValidationResult.Success();
            }
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            Settings settings)
        {
            try
            {
                var maps = await _mapRepository.GetAllAsync();

                await AnsiConsole.Status()
                    .StartAsync("Extracting metadata...", async ctx =>
                    {
                        switch (settings.Type.ToLowerInvariant())
                        {
                            case "popups":
                                var popups = await _metadataService.ExtractPopupsAsync(maps);
                                await SaveMetadata(settings.OutputFile, popups, settings.Format);
                                break;

                            case "warps":
                                var warps = await _metadataService.ExtractWarpsAsync(maps);
                                await SaveMetadata(settings.OutputFile, warps, settings.Format);
                                break;

                            case "all":
                                var allPopups = await _metadataService.ExtractPopupsAsync(maps);
                                var allWarps = await _metadataService.ExtractWarpsAsync(maps);
                                await SaveMetadata(
                                    settings.OutputFile,
                                    new { Popups = allPopups, Warps = allWarps },
                                    settings.Format);
                                break;
                        }
                    });

                AnsiConsole.MarkupLine("[green]Metadata extracted successfully[/]");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Metadata extraction failed");
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                return 1;
            }
        }

        private static async Task SaveMetadata<T>(string path, T data, string format)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                await File.WriteAllTextAsync(path, json);
            }
            else if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                // CSV implementation would go here
                throw new NotImplementedException("CSV format not yet implemented");
            }
        }
    }

    /// <summary>
    /// Validate command - validates map/tileset integrity
    /// </summary>
    public sealed class ValidateCommand : AsyncCommand<ValidateCommand.Settings>
    {
        private readonly IValidationService _validationService;
        private readonly IMapRepository _mapRepository;
        private readonly ITilesetRepository _tilesetRepository;
        private readonly ILogger<ValidateCommand> _logger;

        public ValidateCommand(
            IValidationService validationService,
            IMapRepository mapRepository,
            ITilesetRepository tilesetRepository,
            ILogger<ValidateCommand> logger)
        {
            _validationService = validationService;
            _mapRepository = mapRepository;
            _tilesetRepository = tilesetRepository;
            _logger = logger;
        }

        public sealed class Settings : CommandSettings
        {
            [CommandArgument(0, "<input>")]
            [Description("Input directory to validate")]
            public required string Input { get; init; }

            [CommandOption("--type <TYPE>")]
            [Description("Validation type (maps, tilesets, all)")]
            [DefaultValue("all")]
            public string Type { get; init; } = "all";

            [CommandOption("--strict")]
            [Description("Enable strict validation rules")]
            public bool Strict { get; init; }

            public override ValidationResult Validate()
            {
                if (!Directory.Exists(Input))
                    return ValidationResult.Error($"Input directory not found: {Input}");

                return ValidationResult.Success();
            }
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            Settings settings)
        {
            try
            {
                var options = new ValidationOptions { StrictMode = settings.Strict };
                var hasErrors = false;

                if (settings.Type is "maps" or "all")
                {
                    var maps = await _mapRepository.GetAllAsync();
                    foreach (var map in maps)
                    {
                        var result = await _validationService.ValidateMapAsync(map, options);
                        if (!result.IsValid)
                        {
                            hasErrors = true;
                            DisplayValidationErrors(map.Name, result);
                        }
                    }
                }

                if (settings.Type is "tilesets" or "all")
                {
                    var tilesets = await _tilesetRepository.GetAllAsync();
                    foreach (var tileset in tilesets)
                    {
                        var result = await _validationService.ValidateTilesetAsync(tileset, options);
                        if (!result.IsValid)
                        {
                            hasErrors = true;
                            DisplayValidationErrors(tileset.Name, result);
                        }
                    }
                }

                if (!hasErrors)
                {
                    AnsiConsole.MarkupLine("[green]All validation checks passed[/]");
                    return 0;
                }

                AnsiConsole.MarkupLine("[red]Validation failed with errors[/]");
                return 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation failed");
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                return 1;
            }
        }

        private static void DisplayValidationErrors(string name, ValidationResult result)
        {
            AnsiConsole.MarkupLine($"[yellow]{name}:[/]");
            foreach (var error in result.Errors)
            {
                AnsiConsole.MarkupLine($"  [red]•[/] {error.Message}");
            }
        }
    }
}
```

---

## 5. Service Layer Implementation

### 5.1 Map Conversion Service

```csharp
namespace Porycon2.Core.Services.Conversion
{
    public sealed class MapConversionService : IMapConversionService
    {
        private readonly IMapConverter _converter;
        private readonly IMapRepository _repository;
        private readonly IValidationService _validationService;
        private readonly ILogger<MapConversionService> _logger;

        public MapConversionService(
            IMapConverter converter,
            IMapRepository repository,
            IValidationService validationService,
            ILogger<MapConversionService> logger)
        {
            _converter = converter;
            _repository = repository;
            _validationService = validationService;
            _logger = logger;
        }

        public async Task<ConversionResult<MapDefinition>> ConvertMapAsync(
            string sourcePath,
            string outputPath,
            MapConversionOptions options,
            IProgress<ConversionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                progress?.Report(new ConversionProgress
                {
                    Stage = ConversionStage.Reading,
                    Message = "Reading source file"
                });

                var source = await MapSource.LoadAsync(sourcePath, cancellationToken);

                progress?.Report(new ConversionProgress
                {
                    Stage = ConversionStage.Validating,
                    Message = "Validating source"
                });

                var validationResult = await _converter.ValidateAsync(source, cancellationToken);
                if (!validationResult.IsValid)
                {
                    return ConversionResult<MapDefinition>.Failure(
                        validationResult.Errors.Select(e => e.Message));
                }

                progress?.Report(new ConversionProgress
                {
                    Stage = ConversionStage.Converting,
                    Message = "Converting map"
                });

                var context = new ConversionContext
                {
                    SourcePath = sourcePath,
                    OutputPath = outputPath,
                    Options = options
                };

                var map = await _converter.ConvertAsync(source, context, cancellationToken);

                if (options.ValidateOutput)
                {
                    progress?.Report(new ConversionProgress
                    {
                        Stage = ConversionStage.Validating,
                        Message = "Validating output"
                    });

                    var outputValidation = await _validationService.ValidateMapAsync(
                        map,
                        new ValidationOptions(),
                        cancellationToken);

                    if (!outputValidation.IsValid)
                    {
                        return ConversionResult<MapDefinition>.Failure(
                            outputValidation.Errors.Select(e => e.Message));
                    }
                }

                progress?.Report(new ConversionProgress
                {
                    Stage = ConversionStage.Writing,
                    Message = "Writing output"
                });

                await _repository.AddAsync(map, cancellationToken);

                progress?.Report(new ConversionProgress
                {
                    Stage = ConversionStage.Complete,
                    Message = "Conversion complete"
                });

                sw.Stop();

                return ConversionResult<MapDefinition>.Success(map, sw.Elapsed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert map: {SourcePath}", sourcePath);
                return ConversionResult<MapDefinition>.Failure(new[] { ex.Message });
            }
        }

        public async Task<BatchConversionResult> ConvertMapsAsync(
            IEnumerable<string> sourcePaths,
            string outputDirectory,
            MapConversionOptions options,
            IProgress<BatchConversionProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            var pathList = sourcePaths.ToList();
            var results = new ConcurrentBag<ConversionResult<MapDefinition>>();
            var processedCount = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxParallelism,
                CancellationToken = cancellationToken
            };

            await Parallel.ForEachAsync(pathList, parallelOptions, async (sourcePath, ct) =>
            {
                var outputPath = Path.Combine(
                    outputDirectory,
                    Path.GetFileNameWithoutExtension(sourcePath) + ".json");

                var result = await ConvertMapAsync(
                    sourcePath,
                    outputPath,
                    options,
                    null,
                    ct);

                results.Add(result);

                var processed = Interlocked.Increment(ref processedCount);
                progress?.Report(new BatchConversionProgress
                {
                    Total = pathList.Count,
                    Processed = processed,
                    CurrentFile = Path.GetFileName(sourcePath),
                    SuccessCount = results.Count(r => r.IsSuccess),
                    FailedCount = results.Count(r => !r.IsSuccess)
                });
            });

            sw.Stop();

            var successfulResults = results.Where(r => r.IsSuccess).ToList();
            var failedResults = results.Where(r => !r.IsSuccess).ToList();

            return new BatchConversionResult
            {
                TotalFiles = pathList.Count,
                SuccessCount = successfulResults.Count,
                FailedCount = failedResults.Count,
                Duration = sw.Elapsed,
                Errors = failedResults.SelectMany(r => r.Errors).ToList()
            };
        }
    }
}
```

### 5.2 Tileset Service

```csharp
namespace Porycon2.Core.Services.Conversion
{
    public sealed class TilesetService : ITilesetService
    {
        private readonly ITilesetBuilder _builder;
        private readonly ITilesetRepository _repository;
        private readonly ILogger<TilesetService> _logger;

        public TilesetService(
            ITilesetBuilder builder,
            ITilesetRepository repository,
            ILogger<TilesetService> logger)
        {
            _builder = builder;
            _repository = repository;
            _logger = logger;
        }

        public async Task<IReadOnlyList<Tileset>> BuildTilesetsAsync(
            IEnumerable<MapDefinition> maps,
            TilesetBuildOptions options,
            IProgress<TilesetProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var mapList = maps.ToList();
            var tilesetGroups = mapList
                .GroupBy(m => m.TilesetName)
                .Where(g => !options.PrimaryOnly || IsPrimaryTileset(g.Key));

            var tilesets = new List<Tileset>();
            var processedCount = 0;

            foreach (var group in tilesetGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new TilesetProgress
                {
                    Total = tilesetGroups.Count(),
                    Processed = processedCount,
                    CurrentTileset = group.Key
                });

                // Use first map as representative for tileset building
                var representativeMap = group.First();

                var tileset = await _builder.BuildAsync(
                    representativeMap,
                    options,
                    cancellationToken);

                if (options.ScanAnimations)
                {
                    var animations = await _builder.ScanAnimationsAsync(
                        tileset,
                        cancellationToken);

                    tileset = tileset with { Animations = animations };
                }

                if (options.Optimize)
                {
                    var optimizationOptions = new OptimizationOptions
                    {
                        RemoveDuplicates = true,
                        CompressMetatiles = true
                    };

                    tileset = await _builder.OptimizeAsync(
                        tileset,
                        optimizationOptions,
                        cancellationToken);
                }

                // Check for existing tileset with same hash
                var existingTileset = await _repository.GetByHashAsync(
                    tileset.Hash,
                    cancellationToken);

                if (existingTileset != null)
                {
                    _logger.LogInformation(
                        "Tileset {Name} matches existing tileset {ExistingName}",
                        tileset.Name,
                        existingTileset.Name);

                    tilesets.Add(existingTileset);
                }
                else
                {
                    await _repository.AddAsync(tileset, cancellationToken);
                    tilesets.Add(tileset);
                }

                processedCount++;
            }

            return tilesets.AsReadOnly();
        }

        public async Task<AnimationScanResult> ScanAnimationsAsync(
            IEnumerable<Tileset> tilesets,
            AnimationScanOptions options,
            CancellationToken cancellationToken = default)
        {
            var allAnimations = new List<TileAnimation>();

            foreach (var tileset in tilesets)
            {
                var animations = await _builder.ScanAnimationsAsync(
                    tileset,
                    cancellationToken);

                allAnimations.AddRange(animations);
            }

            return new AnimationScanResult
            {
                TotalAnimations = allAnimations.Count,
                Animations = allAnimations.AsReadOnly(),
                AnimationsByTileset = allAnimations
                    .GroupBy(a => a.Id)
                    .ToDictionary(g => g.Key, g => g.ToList().AsReadOnly())
            };
        }

        public async Task<OptimizationResult> OptimizeTilesetAsync(
            Tileset tileset,
            OptimizationOptions options,
            CancellationToken cancellationToken = default)
        {
            var originalSize = tileset.Metatiles.Count;

            var optimized = await _builder.OptimizeAsync(
                tileset,
                options,
                cancellationToken);

            var newSize = optimized.Metatiles.Count;
            var reduction = originalSize - newSize;
            var reductionPercentage = (double)reduction / originalSize * 100;

            return new OptimizationResult
            {
                OriginalMetatileCount = originalSize,
                OptimizedMetatileCount = newSize,
                MetatilesRemoved = reduction,
                ReductionPercentage = reductionPercentage,
                OptimizedTileset = optimized
            };
        }

        private static bool IsPrimaryTileset(string name)
        {
            // Convention: primary tilesets start with "primary_"
            return name.StartsWith("primary_", StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

---

## 6. Pipeline Pattern with TPL DataFlow

### 6.1 Conversion Pipeline

```csharp
namespace Porycon2.Core.Pipelines
{
    /// <summary>
    /// TPL DataFlow pipeline for high-throughput map conversion
    /// </summary>
    public sealed class MapConversionPipeline : IDisposable
    {
        private readonly TransformBlock<string, MapSource> _loadBlock;
        private readonly TransformBlock<MapSource, ValidationResult> _validateBlock;
        private readonly TransformBlock<(MapSource Source, ValidationResult Validation), MapDefinition> _convertBlock;
        private readonly ActionBlock<MapDefinition> _saveBlock;

        private readonly IMapConverter _converter;
        private readonly IMapRepository _repository;
        private readonly IValidationService _validationService;

        public MapConversionPipeline(
            IMapConverter converter,
            IMapRepository repository,
            IValidationService validationService,
            int maxParallelism = 4)
        {
            _converter = converter;
            _repository = repository;
            _validationService = validationService;

            var parallelOptions = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                BoundedCapacity = maxParallelism * 2
            };

            // Stage 1: Load source files
            _loadBlock = new TransformBlock<string, MapSource>(
                async path => await MapSource.LoadAsync(path),
                parallelOptions);

            // Stage 2: Validate sources
            _validateBlock = new TransformBlock<MapSource, ValidationResult>(
                async source => await _validationService.ValidateMapAsync(source),
                parallelOptions);

            // Stage 3: Convert to target format
            _convertBlock = new TransformBlock<(MapSource, ValidationResult), MapDefinition>(
                async tuple =>
                {
                    var (source, validation) = tuple;
                    if (!validation.IsValid)
                        throw new ValidationException(validation.Errors);

                    var context = new ConversionContext();
                    return await _converter.ConvertAsync(source, context);
                },
                parallelOptions);

            // Stage 4: Save to repository
            _saveBlock = new ActionBlock<MapDefinition>(
                async map => await _repository.AddAsync(map),
                parallelOptions);

            // Link pipeline stages
            _loadBlock.LinkTo(_validateBlock, new DataflowLinkOptions { PropagateCompletion = true });
            _validateBlock.LinkTo(_convertBlock, new DataflowLinkOptions { PropagateCompletion = true });
            _convertBlock.LinkTo(_saveBlock, new DataflowLinkOptions { PropagateCompletion = true });
        }

        public async Task ProcessAsync(
            IEnumerable<string> sourcePaths,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var processedCount = 0;

            foreach (var path in sourcePaths)
            {
                await _loadBlock.SendAsync(path, cancellationToken);
                progress?.Report(Interlocked.Increment(ref processedCount));
            }

            _loadBlock.Complete();
            await _saveBlock.Completion;
        }

        public void Dispose()
        {
            _loadBlock.Complete();
            _saveBlock.Completion.Wait();
        }
    }
}
```

---

## 7. Dependency Injection Configuration

### 7.1 Service Registration

```csharp
namespace Porycon2.Cli.Infrastructure
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPorycon2Services(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Configuration
            services.Configure<MapConversionOptions>(
                configuration.GetSection("MapConversion"));
            services.Configure<TilesetBuildOptions>(
                configuration.GetSection("TilesetBuilding"));
            services.Configure<AudioConversionOptions>(
                configuration.GetSection("AudioConversion"));

            // Core Services
            services.AddSingleton<IMapConversionService, MapConversionService>();
            services.AddSingleton<ITilesetService, TilesetService>();
            services.AddSingleton<IAudioConversionService, AudioConversionService>();
            services.AddSingleton<IMetadataExtractionService, MetadataExtractionService>();
            services.AddSingleton<IValidationService, ValidationService>();

            // Converters
            services.AddSingleton<IMapConverter, PoryMapConverter>();
            services.AddSingleton<ITilesetBuilder, TilesetBuilder>();
            services.AddSingleton<IWorldBuilder, WorldBuilder>();
            services.AddSingleton<IAudioConverter, AudioConverter>();

            // Repositories
            services.AddSingleton<IMapRepository, JsonMapRepository>();
            services.AddSingleton<ITilesetRepository, JsonTilesetRepository>();
            services.AddSingleton<IFileRepository, FileSystemRepository>();

            // Pipelines
            services.AddTransient<MapConversionPipeline>();

            // Commands
            services.AddSingleton<ConvertCommand>();
            services.AddSingleton<BuildTilesetsCommand>();
            services.AddSingleton<ExtractAudioCommand>();
            services.AddSingleton<ExtractMetadataCommand>();
            services.AddSingleton<ValidateCommand>();

            return services;
        }
    }

    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables("PORYCON2_")
                .Build();

            var services = new ServiceCollection();

            // Logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddConfiguration(configuration.GetSection("Logging"));
            });

            // Porycon2 services
            services.AddPorycon2Services(configuration);

            // Build service provider
            var serviceProvider = services.BuildServiceProvider();

            // Configure Spectre.Console.Cli
            var app = new CommandApp(new TypeRegistrar(serviceProvider));

            app.Configure(config =>
            {
                config.SetApplicationName("porycon2");
                config.ValidateExamples();

                config.AddCommand<ConvertCommand>("convert")
                    .WithDescription("Convert maps to target format")
                    .WithExample(new[] { "convert", "input/maps", "output/maps", "--parallel", "8" });

                config.AddCommand<BuildTilesetsCommand>("build-tilesets")
                    .WithDescription("Build tilesets from map definitions")
                    .WithExample(new[] { "build-tilesets", "maps", "tilesets", "--scan-animations" });

                config.AddCommand<ExtractAudioCommand>("extract-audio")
                    .WithDescription("Extract and convert audio files")
                    .WithExample(new[] { "extract-audio", "input/audio", "output/audio", "-f", "wav" });

                config.AddCommand<ExtractMetadataCommand>("extract-metadata")
                    .WithDescription("Extract metadata from maps")
                    .WithExample(new[] { "extract-metadata", "maps", "metadata.json", "--type", "popups" });

                config.AddCommand<ValidateCommand>("validate")
                    .WithDescription("Validate map and tileset integrity")
                    .WithExample(new[] { "validate", "output", "--strict" });
            });

            return await app.RunAsync(args);
        }
    }

    /// <summary>
    /// Type registrar for Spectre.Console.Cli DI integration
    /// </summary>
    public sealed class TypeRegistrar : ITypeRegistrar
    {
        private readonly IServiceProvider _provider;

        public TypeRegistrar(IServiceProvider provider)
        {
            _provider = provider;
        }

        public ITypeResolver Build() => new TypeResolver(_provider);

        public void Register(Type service, Type implementation)
        {
            // Not needed - we handle registration in ServiceCollection
        }

        public void RegisterInstance(Type service, object implementation)
        {
            // Not needed - we handle registration in ServiceCollection
        }

        public void RegisterLazy(Type service, Func<object> factory)
        {
            // Not needed - we handle registration in ServiceCollection
        }
    }

    public sealed class TypeResolver : ITypeResolver
    {
        private readonly IServiceProvider _provider;

        public TypeResolver(IServiceProvider provider)
        {
            _provider = provider;
        }

        public object? Resolve(Type? type)
        {
            return type == null ? null : _provider.GetService(type);
        }
    }
}
```

---

## 8. Configuration Structure

### 8.1 appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "System": "Warning"
    }
  },
  "MapConversion": {
    "OutputFormat": "json",
    "MaxParallelism": 4,
    "Recursive": true,
    "OverwriteExisting": false,
    "ValidateOutput": true,
    "PreserveSourceMetadata": true
  },
  "TilesetBuilding": {
    "ScanAnimations": true,
    "Optimize": true,
    "PrimaryOnly": false,
    "MaxMetatilesPerTileset": 1024,
    "RemoveDuplicates": true
  },
  "AudioConversion": {
    "SampleRate": 44100,
    "Channels": 2,
    "BitsPerSample": 16,
    "PreserveLoops": true,
    "NormalizeVolume": false
  },
  "Validation": {
    "StrictMode": false,
    "ValidateTileReferences": true,
    "ValidateConnections": true,
    "ValidateWarps": true
  }
}
```

---

## 9. Quality Attributes & Non-Functional Requirements

### 9.1 Performance

**Requirements:**
- Batch conversion of 1000 maps in < 60 seconds
- Parallel processing up to 16 concurrent operations
- Memory footprint < 500MB for typical workloads
- Streaming processing for large files (> 100MB)

**Implementation:**
- TPL DataFlow for pipeline parallelism
- Async I/O throughout
- Memory pooling for byte buffers (ArrayPool<byte>)
- Lazy loading for large collections

### 9.2 Maintainability

**Requirements:**
- < 500 lines per file (enforced by analyzer)
- > 80% code coverage
- Clear separation of concerns
- Comprehensive XML documentation

**Implementation:**
- Interface-based design
- SOLID principles
- Domain-Driven Design patterns
- Extensive unit tests

### 9.3 Extensibility

**Requirements:**
- Plugin architecture for new converters
- Custom validation rules
- Extensible metadata extraction

**Implementation:**
- Strategy pattern for converters
- Factory pattern for format selection
- Event-driven architecture for hooks

### 9.4 Testability

**Requirements:**
- All services testable in isolation
- Mock-friendly interfaces
- Integration test support

**Implementation:**
- Dependency injection
- Repository pattern
- In-memory test doubles

---

## 10. Error Handling Strategy

### 10.1 Exception Hierarchy

```csharp
namespace Porycon2.Domain.Exceptions
{
    public abstract class Porycon2Exception : Exception
    {
        protected Porycon2Exception(string message) : base(message) { }
        protected Porycon2Exception(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class ConversionException : Porycon2Exception
    {
        public ConversionException(string message) : base(message) { }
        public ConversionException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class ValidationException : Porycon2Exception
    {
        public IReadOnlyList<ValidationError> Errors { get; }

        public ValidationException(IEnumerable<ValidationError> errors)
            : base("Validation failed")
        {
            Errors = errors.ToList();
        }
    }

    public sealed class FileFormatException : Porycon2Exception
    {
        public string FilePath { get; }

        public FileFormatException(string filePath, string message)
            : base($"Invalid file format in {filePath}: {message}")
        {
            FilePath = filePath;
        }
    }
}
```

### 10.2 Result Pattern

```csharp
namespace Porycon2.Domain.Models.Common
{
    public sealed class ConversionResult<T>
    {
        public bool IsSuccess { get; }
        public T? Data { get; }
        public IReadOnlyList<string> Errors { get; }
        public TimeSpan Duration { get; }

        private ConversionResult(
            bool isSuccess,
            T? data,
            IEnumerable<string> errors,
            TimeSpan duration)
        {
            IsSuccess = isSuccess;
            Data = data;
            Errors = errors.ToList();
            Duration = duration;
        }

        public static ConversionResult<T> Success(T data, TimeSpan duration)
            => new(true, data, Array.Empty<string>(), duration);

        public static ConversionResult<T> Failure(IEnumerable<string> errors)
            => new(false, default, errors, TimeSpan.Zero);
    }

    public sealed class ValidationResult
    {
        public bool IsValid => !Errors.Any();
        public IReadOnlyList<ValidationError> Errors { get; }

        public ValidationResult(IEnumerable<ValidationError> errors)
        {
            Errors = errors.ToList();
        }

        public static ValidationResult Success()
            => new(Array.Empty<ValidationError>());
    }

    public sealed record ValidationError(string PropertyName, string Message);
}
```

---

## 11. Testing Strategy

### 11.1 Unit Tests

```csharp
namespace Porycon2.Core.Tests.Services
{
    public sealed class MapConversionServiceTests
    {
        private readonly Mock<IMapConverter> _mockConverter;
        private readonly Mock<IMapRepository> _mockRepository;
        private readonly Mock<IValidationService> _mockValidationService;
        private readonly MapConversionService _sut;

        public MapConversionServiceTests()
        {
            _mockConverter = new Mock<IMapConverter>();
            _mockRepository = new Mock<IMapRepository>();
            _mockValidationService = new Mock<IValidationService>();
            _sut = new MapConversionService(
                _mockConverter.Object,
                _mockRepository.Object,
                _mockValidationService.Object,
                Mock.Of<ILogger<MapConversionService>>());
        }

        [Fact]
        public async Task ConvertMapAsync_ValidInput_ReturnsSuccess()
        {
            // Arrange
            var sourcePath = "test.map";
            var outputPath = "test.json";
            var options = new MapConversionOptions();
            var expectedMap = new MapDefinition { /* ... */ };

            _mockConverter
                .Setup(x => x.ValidateAsync(It.IsAny<MapSource>(), default))
                .ReturnsAsync(ValidationResult.Success());

            _mockConverter
                .Setup(x => x.ConvertAsync(It.IsAny<MapSource>(), It.IsAny<ConversionContext>(), default))
                .ReturnsAsync(expectedMap);

            // Act
            var result = await _sut.ConvertMapAsync(sourcePath, outputPath, options);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(expectedMap, result.Data);
            _mockRepository.Verify(x => x.AddAsync(expectedMap, default), Times.Once);
        }

        [Fact]
        public async Task ConvertMapsAsync_MultipleFiles_ProcessesInParallel()
        {
            // Arrange
            var sourcePaths = Enumerable.Range(1, 10).Select(i => $"map{i}.map");
            var outputDirectory = "output";
            var options = new MapConversionOptions { MaxParallelism = 4 };

            // Act
            var sw = Stopwatch.StartNew();
            var result = await _sut.ConvertMapsAsync(sourcePaths, outputDirectory, options);
            sw.Stop();

            // Assert
            Assert.Equal(10, result.TotalFiles);
            // Verify parallel execution by checking duration
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5)); // Should be faster than sequential
        }
    }
}
```

### 11.2 Integration Tests

```csharp
namespace Porycon2.Integration.Tests
{
    public sealed class EndToEndConversionTests : IDisposable
    {
        private readonly string _testDataDirectory;
        private readonly string _outputDirectory;
        private readonly IServiceProvider _serviceProvider;

        public EndToEndConversionTests()
        {
            _testDataDirectory = Path.Combine(Path.GetTempPath(), "porycon2-test-data");
            _outputDirectory = Path.Combine(Path.GetTempPath(), "porycon2-output");

            Directory.CreateDirectory(_testDataDirectory);
            Directory.CreateDirectory(_outputDirectory);

            var services = new ServiceCollection();
            services.AddPorycon2Services(new ConfigurationBuilder().Build());
            services.AddLogging();
            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public async Task ConvertRealPoryMapFile_ProducesValidOutput()
        {
            // Arrange
            var testMapPath = Path.Combine(_testDataDirectory, "route101.json");
            await File.WriteAllTextAsync(testMapPath, GetSamplePoryMapJson());

            var conversionService = _serviceProvider.GetRequiredService<IMapConversionService>();
            var options = new MapConversionOptions();

            // Act
            var result = await conversionService.ConvertMapAsync(
                testMapPath,
                Path.Combine(_outputDirectory, "route101.json"),
                options);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Data);
            Assert.Equal("route101", result.Data.Id);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDataDirectory))
                Directory.Delete(_testDataDirectory, true);
            if (Directory.Exists(_outputDirectory))
                Directory.Delete(_outputDirectory, true);
        }

        private static string GetSamplePoryMapJson() => @"{
            ""id"": ""route101"",
            ""name"": ""Route 101"",
            ""width"": 20,
            ""height"": 15,
            ""tileset_primary"": ""gTileset_General"",
            ""tileset_secondary"": ""gTileset_Petalburg""
        }";
    }
}
```

---

## 12. Deployment & Build

### 12.1 Project File (Porycon2.Cli.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>porycon2</AssemblyName>
    <RootNamespace>Porycon2.Cli</RootNamespace>
    <Version>2.0.0</Version>
    <Authors>Your Team</Authors>
    <Description>Pokemon map conversion tool</Description>

    <!-- Enable trimming for smaller deployment -->
    <PublishTrimmed>true</PublishTrimmed>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Spectre.Console.Cli" Version="0.49.0" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
    <PackageReference Include="System.Threading.Tasks.Dataflow" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Porycon2.Core\Porycon2.Core.csproj" />
    <ProjectReference Include="..\Porycon2.Domain\Porycon2.Domain.csproj" />
    <ProjectReference Include="..\Porycon2.Infrastructure\Porycon2.Infrastructure.csproj" />
    <ProjectReference Include="..\Porycon2.Converters\Porycon2.Converters.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

---

## 13. Implementation Roadmap

### Phase 1: Foundation (Week 1-2)
- [ ] Project structure setup
- [ ] Domain models (Maps, Tilesets)
- [ ] Core interfaces (IMapConverter, ITilesetBuilder)
- [ ] Basic DI configuration
- [ ] Unit test infrastructure

### Phase 2: Core Conversion (Week 3-4)
- [ ] PoryMap format parser
- [ ] Map converter implementation
- [ ] Tileset builder
- [ ] File repositories (JSON)
- [ ] Basic CLI commands (convert, build-tilesets)

### Phase 3: Advanced Features (Week 5-6)
- [ ] Animation scanner
- [ ] Metadata extraction
- [ ] Audio conversion
- [ ] World/Region builder
- [ ] Validation service

### Phase 4: Performance & Polish (Week 7-8)
- [ ] TPL DataFlow pipelines
- [ ] Parallel batch processing
- [ ] Progress reporting
- [ ] Error handling refinement
- [ ] Integration tests

### Phase 5: Documentation & Release (Week 9)
- [ ] User documentation
- [ ] API documentation
- [ ] Migration guide from Python tool
- [ ] Performance benchmarks
- [ ] Release v2.0.0

---

## 14. Migration from Python Tool

### 14.1 Feature Parity Matrix

| Python Feature | C# Equivalent | Status | Notes |
|---------------|---------------|--------|-------|
| Map conversion | MapConversionService | ✓ | Enhanced with validation |
| Tileset building | TilesetService | ✓ | Added optimization |
| Animation scan | AnimationScannerService | ✓ | Async implementation |
| Audio extraction | AudioConversionService | ✓ | Added format options |
| Popup extraction | MetadataExtractionService | ✓ | Generalized metadata |
| Batch processing | Parallel.ForEachAsync | ✓ | Better parallelism |
| Progress reporting | IProgress<T> | ✓ | Typed progress |
| CLI interface | Spectre.Console.Cli | ✓ | Enhanced UX |

### 14.2 Breaking Changes

1. **Command Structure**: Commands use subcommands instead of flags
   - Python: `porycon2 --convert input output`
   - C#: `porycon2 convert input output`

2. **Configuration**: Uses appsettings.json instead of CLI arguments
   - Allows environment-specific overrides
   - Better for CI/CD integration

3. **Output Format**: Default is structured JSON
   - More consistent than Python's mixed output
   - Easier to parse programmatically

---

## Conclusion

This architecture provides a solid foundation for a maintainable, performant, and extensible C# rewrite of porycon2. Key strengths:

1. **Modularity**: Clear separation between CLI, Core, Domain, and Infrastructure
2. **Async/Parallel**: TPL DataFlow and async/await throughout
3. **Testability**: Interface-based design with DI
4. **Extensibility**: Plugin architecture for converters
5. **User Experience**: Spectre.Console.Cli provides excellent CLI UX
6. **Performance**: Parallel processing, memory pooling, streaming I/O

The design follows SOLID principles, Domain-Driven Design, and modern .NET best practices. It's ready for implementation following the roadmap provided.
