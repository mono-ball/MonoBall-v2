using Porycon3.Models;
using Porycon3.Infrastructure;
using Porycon3.Services.Builders;
using Porycon3.Services.Interfaces;
using Porycon3.Services.Sound;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Spectre.Console;
using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services;

public class MapConversionService : IMapConversionService
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly string _region;
    private readonly bool _verbose;

    private readonly MapJsonReader _mapReader;
    private readonly MetatileBinReader _metatileReader;
    private readonly MapBinReader _mapBinReader;
    private readonly DefinitionGenerator _definitionGenerator;
    private readonly MapSectionExtractor _sectionExtractor;
    private readonly PopupExtractor _popupExtractor;
    private readonly WeatherExtractor _weatherExtractor;
    private readonly BattleEnvironmentExtractor _battleEnvExtractor;
    private readonly SpriteExtractor _spriteExtractor;
    private readonly TextWindowExtractor _textWindowExtractor;
    private readonly PokemonExtractor _pokemonExtractor;
    private readonly SpeciesExtractor _speciesExtractor;
    private readonly FieldEffectExtractor _fieldEffectExtractor;
    private readonly DoorAnimationExtractor _doorAnimExtractor;
    private readonly BehaviorExtractor _behaviorExtractor;
    private readonly ScriptExtractor _scriptExtractor;
    private readonly SoundExtractor _soundExtractor;
    private readonly FontExtractor _fontExtractor;
    private readonly InterfaceExtractor _interfaceExtractor;

    // Builders for output generation
    private readonly MapOutputBuilder _outputBuilder;
    private readonly TilesheetOutputBuilder _tilesheetBuilder;

    // Shared tileset registry - reuses tilesets across maps with same tileset pair
    private readonly SharedTilesetRegistry _tilesetRegistry;

    // Pending map data - maps are written after tileset finalization to get correct GID offsets
    private readonly List<PendingMapData> _pendingMaps = new();

    public MapConversionService(
        string inputPath,
        string outputPath,
        string region,
        bool verbose = false)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        _region = region;
        _verbose = verbose;

        _mapReader = new MapJsonReader(inputPath);
        _metatileReader = new MetatileBinReader(inputPath);
        _mapBinReader = new MapBinReader(inputPath);
        _definitionGenerator = new DefinitionGenerator(inputPath, outputPath, region);
        _sectionExtractor = new MapSectionExtractor(inputPath, outputPath, region);
        _popupExtractor = new PopupExtractor(inputPath, outputPath);
        _weatherExtractor = new WeatherExtractor(inputPath, outputPath);
        _battleEnvExtractor = new BattleEnvironmentExtractor(inputPath, outputPath);
        _spriteExtractor = new SpriteExtractor(inputPath, outputPath, verbose);
        _textWindowExtractor = new TextWindowExtractor(inputPath, outputPath);
        _pokemonExtractor = new PokemonExtractor(inputPath, outputPath, verbose);
        _speciesExtractor = new SpeciesExtractor(inputPath, outputPath, verbose);
        _fieldEffectExtractor = new FieldEffectExtractor(inputPath, outputPath, verbose);
        _doorAnimExtractor = new DoorAnimationExtractor(inputPath, outputPath);
        _behaviorExtractor = new BehaviorExtractor(inputPath, outputPath, verbose);
        _scriptExtractor = new ScriptExtractor(inputPath, outputPath, verbose);
        _soundExtractor = new SoundExtractor(inputPath, outputPath, verbose);
        _fontExtractor = new FontExtractor(inputPath, outputPath, verbose);
        _interfaceExtractor = new InterfaceExtractor(inputPath, outputPath, verbose);
        _outputBuilder = new MapOutputBuilder(region);
        _tilesheetBuilder = new TilesheetOutputBuilder(outputPath);
        _tilesetRegistry = new SharedTilesetRegistry(inputPath);
    }

    public List<string> ScanMaps()
    {
        var mapsDir = Path.Combine(_inputPath, "data", "maps");
        if (!Directory.Exists(mapsDir))
            throw new DirectoryNotFoundException($"Maps directory not found: {mapsDir}");

        return Directory.GetDirectories(mapsDir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(name => name)
            .ToList()!;
    }

    public ConversionResult ConvertMap(string mapName)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Read map definition
            var mapData = _mapReader.ReadMap(mapName);

            // 2. Read metatiles for both tilesets
            var primaryMetatiles = _metatileReader.ReadMetatiles(mapData.Layout.PrimaryTileset);
            var secondaryMetatiles = _metatileReader.ReadMetatiles(mapData.Layout.SecondaryTileset);

            // 3. Read map binary (metatile indices)
            var mapBin = _mapBinReader.ReadMapBin(
                mapData.Layout.Id,
                mapData.Layout.Width,
                mapData.Layout.Height,
                mapData.Layout.BlockdataPath);

            // 4. Get or create shared tileset builder for this tileset pair
            var sharedBuilder = _tilesetRegistry.GetOrCreateBuilder(
                mapData.Layout.PrimaryTileset,
                mapData.Layout.SecondaryTileset);
            _tilesetRegistry.RegisterMapUsage(mapName, mapData.Layout.PrimaryTileset, mapData.Layout.SecondaryTileset);

            var layers = ProcessMapWithSharedTileset(
                mapBin,
                primaryMetatiles,
                secondaryMetatiles,
                mapData.Layout.PrimaryTileset,
                mapData.Layout.SecondaryTileset,
                mapData.Layout.Width,
                mapData.Layout.Height,
                sharedBuilder);

            // Note: Tilesheet is built after all maps are processed via FinalizeSharedTilesets()

            // 5. Build collision layers from map data (one per elevation)
            var collisionLayers = BuildCollisionLayers(mapBin, mapData.Layout.Width, mapData.Layout.Height);

            // 6. Transform and track IDs for definition generation
            var weatherId = MapTransformers.TransformWeatherId(mapData.Metadata.Weather);
            var battleSceneId = MapTransformers.TransformBattleSceneId(mapData.Metadata.BattleScene);
            _definitionGenerator.TrackWeatherId(weatherId);
            _definitionGenerator.TrackBattleSceneId(battleSceneId);

            // 7. Store pending map data (written after tileset finalization for correct GID offsets)
            lock (_pendingMaps)
            {
                _pendingMaps.Add(new PendingMapData(
                    mapName,
                    mapData,
                    layers,
                    sharedBuilder.TilesetPair,
                    collisionLayers,
                    weatherId,
                    battleSceneId));
            }

            sw.Stop();
            return new ConversionResult
            {
                MapId = mapName,
                Success = true,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConversionResult
            {
                MapId = mapName,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    /// <summary>
    /// Process map using shared tileset with flip-aware deduplication.
    /// Creates layer data with GIDs (including flip flags in high bits) referencing the shared tilesheet.
    /// </summary>
    private List<SharedLayerData> ProcessMapWithSharedTileset(
        ushort[] mapBin,
        List<Metatile> primaryMetatiles,
        List<Metatile> secondaryMetatiles,
        string primaryTileset,
        string secondaryTileset,
        int width,
        int height,
        SharedTilesetBuilder builder)
    {
        // Combine metatiles (primary 0-511, secondary 512+)
        var allMetatiles = primaryMetatiles.Concat(secondaryMetatiles).ToList();

        // Layer data: each cell is one metatile (16x16), stored as uint to preserve flip flags
        var bg3Data = new uint[width * height];
        var bg2Data = new uint[width * height];
        var bg1Data = new uint[width * height];

        // Process each metatile position
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var mapIndex = y * width + x;
                var metatileId = MapBinReader.GetMetatileId(mapBin[mapIndex]);

                if (metatileId >= allMetatiles.Count)
                    continue;

                var metatile = allMetatiles[metatileId];

                // Determine which tileset this metatile belongs to
                var isSecondaryMetatile = metatileId >= primaryMetatiles.Count;
                var metatileTileset = isSecondaryMetatile ? secondaryTileset : primaryTileset;

                // Render metatile and get GIDs with flip flags encoded
                var result = builder.ProcessMetatile(metatile, metatileId, metatileTileset);

                // Mark secondary GIDs with marker bit (resolved to actual offset when writing maps)
                var bottomGid = MarkAsSecondary(result.BottomGid, result.IsSecondary);
                var topGid = MarkAsSecondary(result.TopGid, result.IsSecondary);

                // Distribute GIDs to layers based on layer type
                switch (metatile.LayerType)
                {
                    case MetatileLayerType.Normal:
                        // NORMAL: Bottom -> Bg2, Top -> Bg1
                        bg2Data[mapIndex] = bottomGid;
                        bg1Data[mapIndex] = topGid;
                        break;

                    case MetatileLayerType.Covered:
                        // COVERED: Bottom -> Bg3, Top -> Bg2
                        bg3Data[mapIndex] = bottomGid;
                        bg2Data[mapIndex] = topGid;
                        break;

                    case MetatileLayerType.Split:
                        // SPLIT: Bottom -> Bg3, Top -> Bg1
                        bg3Data[mapIndex] = bottomGid;
                        bg1Data[mapIndex] = topGid;
                        break;

                    default:
                        // Default to NORMAL behavior
                        bg2Data[mapIndex] = bottomGid;
                        bg1Data[mapIndex] = topGid;
                        break;
                }
            }
        }

        // Layer elevations based on GBA BG rendering priority:
        // Ground (bg3) = elevation 0 (below player)
        // Objects (bg2) = elevation 3 (player level, where NPCs walk)
        // Overhead (bg1) = elevation 15 (above player, like bridges/tree canopy)
        return new List<SharedLayerData>
        {
            new() { Name = "Ground", Width = width, Height = height, Data = bg3Data, Elevation = 0 },
            new() { Name = "Objects", Width = width, Height = height, Data = bg2Data, Elevation = 3 },
            new() { Name = "Overhead", Width = width, Height = height, Data = bg1Data, Elevation = 15 }
        };
    }

    /// <summary>
    /// Load palettes for a tileset.
    /// </summary>
    private static SixLabors.ImageSharp.PixelFormats.Rgba32[]?[]? LoadPalettes(TilesetPathResolver resolver, string tilesetName)
    {
        var result = resolver.FindTilesetPath(tilesetName);
        if (result == null) return null;
        return PaletteLoader.LoadTilesetPalettes(result.Value.Path);
    }

    /// <summary>
    /// Write all pending maps after tileset finalization provides actual tile counts.
    /// </summary>
    public int WriteAllPendingMaps()
    {
        int count = 0;
        var outputDir = Path.Combine(_outputPath, "Definitions", "Entities", "Maps", _region);
        Directory.CreateDirectory(outputDir);

        foreach (var pending in _pendingMaps)
        {
            // Get tile counts and types for this tileset pair
            var builder = _tilesetRegistry.GetBuilder(pending.TilesetPair);
            var primaryTileCount = builder?.PrimaryTileCount ?? 0;
            var primaryTilesetType = builder?.PrimaryTilesetType ?? "primary";
            var secondaryTilesetType = builder?.SecondaryTilesetType ?? "secondary";

            // Resolve secondary markers in layer data
            var resolvedLayers = ResolveLayers(pending.Layers, primaryTileCount);

            // Build and write map output
            var output = _outputBuilder.BuildMapOutput(
                pending.MapName,
                pending.MapData,
                resolvedLayers,
                pending.TilesetPair,
                primaryTileCount,
                primaryTilesetType,
                secondaryTilesetType,
                pending.CollisionLayers,
                pending.WeatherId,
                pending.BattleSceneId);

            var outputPath = Path.Combine(outputDir, $"{pending.MapName}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(outputPath, json);
            count++;
        }

        _pendingMaps.Clear();
        _tilesetRegistry.Dispose(); // Safe to dispose now that all maps are written
        return count;
    }

    /// <summary>
    /// Resolve secondary markers in layer data to actual offsets.
    /// </summary>
    private List<SharedLayerData> ResolveLayers(List<SharedLayerData> layers, int primaryTileCount)
    {
        return layers.Select(layer => new SharedLayerData
        {
            Name = layer.Name,
            Width = layer.Width,
            Height = layer.Height,
            Elevation = layer.Elevation,
            Data = layer.Data.Select(gid => ResolveSecondaryOffset(gid, primaryTileCount)).ToArray()
        }).ToList();
    }

    /// <summary>
    /// Finalize shared tilesets: process animations and save tilesheet images/definitions.
    /// Call this after all maps have been converted.
    /// </summary>
    public int FinalizeSharedTilesets()
    {
        var resolver = new TilesetPathResolver(_inputPath);
        int count = 0;

        // Process animations for each tileset pair
        foreach (var pair in _tilesetRegistry.GetAllTilesetPairs())
        {
            var builder = _tilesetRegistry.GetBuilder(pair);
            if (builder == null) continue;

            var primaryPalettes = LoadPalettes(resolver, pair.PrimaryTileset);
            var secondaryPalettes = LoadPalettes(resolver, pair.SecondaryTileset);
            builder.ProcessAnimations(primaryPalettes, secondaryPalettes);
        }

        // Build and save individual tilesets using builder
        foreach (var result in _tilesetRegistry.BuildAllTilesets())
        {
            if (result.TileCount == 0) continue;

            _tilesheetBuilder.SaveTilesheet(result);
            count++;
        }

        // Note: Don't dispose registry here - WriteAllPendingMaps() needs tile counts
        return count;
    }

    /// <summary>
    /// Generate additional definitions (Weather, BattleScenes, Region, Sprites, Pokemon, Species) based on IDs
    /// referenced by converted maps. Call this after all maps have been converted.
    /// Uses ExtractionOrchestrator for unified live progress display.
    /// </summary>
    public Dictionary<string, Extraction.ExtractionResult> GenerateDefinitions()
    {
        // Run legacy definition generator with status indicator
        AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .Start("Generating base definitions...", _ =>
            {
                _definitionGenerator.GenerateAll();
            });

        // Use orchestrator for all extractors with unified live display
        var orchestrator = new Extraction.ExtractionOrchestrator(_verbose)
            .Add(_sectionExtractor)
            .Add(_popupExtractor)
            .Add(_weatherExtractor)
            .Add(_battleEnvExtractor)
            .Add(_spriteExtractor)
            .Add(_textWindowExtractor)
            .Add(_pokemonExtractor)
            .Add(_speciesExtractor)
            .Add(_fieldEffectExtractor)
            .Add(_doorAnimExtractor)
            .Add(_behaviorExtractor)
            .Add(_scriptExtractor);

        return orchestrator.RunAll();
    }

    /// <summary>
    /// Get all extractors for external orchestration.
    /// Use this when you need to run extractors with custom progress reporting.
    /// </summary>
    public IEnumerable<Extraction.IExtractor> GetExtractors()
    {
        yield return _sectionExtractor;
        yield return _popupExtractor;
        yield return _weatherExtractor;
        yield return _battleEnvExtractor;
        yield return _spriteExtractor;
        yield return _textWindowExtractor;
        yield return _pokemonExtractor;
        yield return _speciesExtractor;
        yield return _fieldEffectExtractor;
        yield return _doorAnimExtractor;
        yield return _behaviorExtractor;
        yield return _scriptExtractor;
        yield return _soundExtractor;
        yield return _fontExtractor;
        yield return _interfaceExtractor;
    }

    /// <summary>
    /// Run the definition generator (Weather, BattleScene definitions).
    /// Call this before running extractors when using external orchestration.
    /// </summary>
    public void RunDefinitionGenerator()
    {
        _definitionGenerator.GenerateAll();
    }

    /// <summary>
    /// Build collision layers from map binary data, one per elevation level.
    /// Each elevation with collision data gets its own layer containing only collision values (0-3).
    /// </summary>
    private static List<CollisionLayerData> BuildCollisionLayers(ushort[] mapBin, int width, int height)
    {
        // Group tiles by elevation and check if any collision data exists at that elevation
        var elevationData = new Dictionary<int, byte[]>();

        for (int i = 0; i < mapBin.Length; i++)
        {
            var entry = mapBin[i];
            var collision = Infrastructure.MapBinReader.GetCollision(entry);
            var elevation = Infrastructure.MapBinReader.GetElevation(entry);

            // Only create layer if there's actual collision data (collision > 0)
            if (collision > 0)
            {
                if (!elevationData.TryGetValue(elevation, out var data))
                {
                    data = new byte[width * height];
                    elevationData[elevation] = data;
                }
                data[i] = (byte)(collision & 0x3);
            }
        }

        // Create collision layer for each elevation that has collision data
        return elevationData
            .OrderBy(kv => kv.Key)
            .Select(kv => new CollisionLayerData(width, height, kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>
    /// Mark a GID as secondary by setting the SecondaryMarker bit.
    /// The actual offset is resolved later when writing maps, based on actual primary tile count.
    /// </summary>
    private static uint MarkAsSecondary(uint gid, bool isSecondary)
    {
        if (!isSecondary || gid == 0)
            return gid;

        // Set the secondary marker bit (will be resolved to actual offset when writing)
        return gid | SecondaryMarker;
    }

    /// <summary>
    /// Resolve secondary marker to actual offset based on primary tile count.
    /// Called when writing map output after tileset finalization.
    /// </summary>
    private static uint ResolveSecondaryOffset(uint gid, int primaryTileCount)
    {
        if ((gid & SecondaryMarker) == 0)
            return gid;

        // Extract flip flags, secondary marker, and base GID
        var flipFlags = gid & (FlipHorizontal | FlipVertical | FlipDiagonal);
        var baseGid = gid & GidMask;

        // Add offset (primary count) to base GID, combine with flip flags (without marker)
        return flipFlags | (baseGid + (uint)primaryTileCount);
    }
}

/// <summary>
/// Stores map data pending write until tileset finalization provides actual tile counts.
/// </summary>
public record PendingMapData(
    string MapName,
    MapData MapData,
    List<SharedLayerData> Layers,
    TilesetPairKey TilesetPair,
    List<CollisionLayerData> CollisionLayers,
    string? WeatherId,
    string? BattleSceneId
);
