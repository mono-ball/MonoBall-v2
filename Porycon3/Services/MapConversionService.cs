using Porycon3.Models;
using Porycon3.Infrastructure;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Spectre.Console;

namespace Porycon3.Services;

public class MapConversionService
{
    private const int NumMetatilesInPrimary = 512;
    private const int MetatileSize = 16;
    private const int TilesPerRow = 16;

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

    // Shared tileset registry - reuses tilesets across maps with same tileset pair
    private readonly SharedTilesetRegistry _tilesetRegistry;

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

            // 5. Extract collision overrides from map data
            var collisionOverrides = ExtractCollisionOverrides(mapBin, mapData.Layout.Width, mapData.Layout.Height);

            // 6. Write map output (references shared tileset)
            WriteOutput(mapName, mapData, layers, sharedBuilder.TilesetPair, collisionOverrides);

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

                // Distribute GIDs to layers based on layer type
                switch (metatile.LayerType)
                {
                    case MetatileLayerType.Normal:
                        // NORMAL: Bottom -> Bg2, Top -> Bg1
                        bg2Data[mapIndex] = result.BottomGid;
                        bg1Data[mapIndex] = result.TopGid;
                        break;

                    case MetatileLayerType.Covered:
                        // COVERED: Bottom -> Bg3, Top -> Bg2
                        bg3Data[mapIndex] = result.BottomGid;
                        bg2Data[mapIndex] = result.TopGid;
                        break;

                    case MetatileLayerType.Split:
                        // SPLIT: Bottom -> Bg3, Top -> Bg1
                        bg3Data[mapIndex] = result.BottomGid;
                        bg1Data[mapIndex] = result.TopGid;
                        break;

                    default:
                        // Default to NORMAL behavior
                        bg2Data[mapIndex] = result.BottomGid;
                        bg1Data[mapIndex] = result.TopGid;
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

    private void WriteOutput(string mapName, MapData mapData, List<SharedLayerData> layers, TilesetPairKey tilesetPair, List<CollisionOverride> collisionOverrides)
    {
        var outputDir = Path.Combine(_outputPath, "Definitions", "Entities", "Maps", _region);
        Directory.CreateDirectory(outputDir);

        var normalizedName = IdTransformer.Normalize(mapName);
        var outputPath = Path.Combine(outputDir, $"{mapName}.json");

        // Transform and track IDs for definition generation
        var weatherId = TransformWeatherId(mapData.Metadata.Weather);
        var battleSceneId = TransformBattleSceneId(mapData.Metadata.BattleScene);
        _definitionGenerator.TrackWeatherId(weatherId);
        _definitionGenerator.TrackBattleSceneId(battleSceneId);

        var output = new
        {
            id = IdTransformer.MapIdFromName(mapName, _region),
            name = mapData.Name,
            description = "",
            regionId = $"{IdTransformer.Namespace}:region:{_region}",
            mapTypeId = IdTransformer.MapTypeId(mapData.Metadata.MapType),
            width = mapData.Layout.Width,
            height = mapData.Layout.Height,
            tileWidth = MetatileSize,
            tileHeight = MetatileSize,
            musicId = IdTransformer.AudioId(mapData.Metadata.Music),
            weatherId,
            battleSceneId,
            sectionId = IdTransformer.MapsecId(mapData.Metadata.RegionMapSection, _region),
            showMapName = mapData.Metadata.ShowMapName,
            canFly = false,
            requiresFlash = mapData.Metadata.RequiresFlash,
            allowRunning = mapData.Metadata.AllowRunning,
            allowCycling = mapData.Metadata.AllowCycling,
            allowEscaping = mapData.Metadata.AllowEscaping,
            connections = BuildConnections(mapData.Connections),
            encounterDataJson = (string?)null,
            customPropertiesJson = (string?)null,
            layers = layers.Select((l, idx) => new
            {
                id = $"{IdTransformer.Namespace}:layer:{_region}/{normalizedName}/{l.Name.ToLowerInvariant()}",
                name = l.Name,
                type = "tilelayer",
                width = l.Width,
                height = l.Height,
                elevation = l.Elevation,
                visible = true,
                opacity = 1,
                offsetX = 0,
                offsetY = 0,
                tileData = EncodeTileDataUint(l.Data),
                imagePath = (string?)null
            }),
            tilesets = new[]
            {
                new { firstGid = 1, tilesetId = SharedTilesetRegistry.GenerateTilesetId(tilesetPair) }
            },
            warps = mapData.Warps.Select((w, idx) =>
            {
                // Strip MAP_ prefix before normalizing
                var destMapName = w.DestMap.StartsWith("MAP_", StringComparison.OrdinalIgnoreCase)
                    ? w.DestMap[4..]
                    : w.DestMap;
                var destNormalized = IdTransformer.Normalize(destMapName);
                return new
                {
                    id = $"{IdTransformer.Namespace}:warp:{_region}/{normalizedName}/warp_to_{destNormalized}",
                    name = $"Warp to {destNormalized}",
                    x = w.X * MetatileSize,
                    y = w.Y * MetatileSize,
                    width = MetatileSize,
                    height = MetatileSize,
                    targetMapId = IdTransformer.MapId(w.DestMap, _region),
                    targetX = w.DestWarpId,
                    targetY = 0,
                    elevation = w.Elevation
                };
            }),
            triggers = mapData.CoordEvents.Select((c, idx) =>
            {
                var varNormalized = IdTransformer.Normalize(c.Var);
                var value = int.TryParse(c.VarValue, out var v) ? v : 0;
                return new
                {
                    id = $"{IdTransformer.Namespace}:trigger:{_region}/{normalizedName}/trigger_{varNormalized}_{value}",
                    name = $"Trigger: {c.Var} == {c.VarValue}",
                    x = c.X * MetatileSize,
                    y = c.Y * MetatileSize,
                    width = MetatileSize,
                    height = MetatileSize,
                    variable = $"{IdTransformer.Namespace}:variable:{_region}/{c.Var.ToLowerInvariant()}",
                    value,
                    triggerId = TransformTriggerId(c.Script),
                    elevation = c.Elevation
                };
            }),
            interactions = mapData.BgEvents.Select((b, idx) =>
            {
                var scriptNormalized = IdTransformer.Normalize(b.Script).Replace("_", "");
                var typeDisplay = char.ToUpper(b.Type[0]) + b.Type[1..].ToLowerInvariant();
                return new
                {
                    id = $"{IdTransformer.Namespace}:interaction:{_region}/{normalizedName}/{b.Type.ToLowerInvariant()}_{scriptNormalized}",
                    name = $"{typeDisplay}: {b.Script}",
                    x = b.X * MetatileSize,
                    y = b.Y * MetatileSize,
                    width = MetatileSize,
                    height = MetatileSize,
                    interactionId = TransformInteractionId(b.Script),
                    elevation = b.Elevation
                };
            }),
            npcs = mapData.ObjectEvents.Select((o, idx) => new
            {
                id = $"{IdTransformer.Namespace}:npc:{_region}/{normalizedName}/{(o.LocalId ?? $"npc_{idx}").ToLowerInvariant()}",
                name = o.LocalId ?? $"NPC_{idx}",
                x = o.X * MetatileSize,
                y = o.Y * MetatileSize,
                spriteId = IdTransformer.SpriteId(o.GraphicsId),
                behaviorId = TransformBehaviorId(o.MovementType),
                behaviorParameters = BuildBehaviorParameters(o.MovementType, o.X * MetatileSize, o.Y * MetatileSize, o.MovementRangeX, o.MovementRangeY),
                interactionId = TransformInteractionId(o.Script),
                visibilityFlag = string.IsNullOrEmpty(o.Flag) || o.Flag == "0" ? null : IdTransformer.FlagId(o.Flag),
                facingDirection = ExtractDirection(o.MovementType),
                elevation = o.Elevation
            }),
            collision = collisionOverrides.Count > 0 ? collisionOverrides.Select(c => new
            {
                x = c.X,
                y = c.Y,
                width = MetatileSize,
                height = MetatileSize,
                elevation = c.Elevation
            }) : null
        };

        var json = System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        File.WriteAllText(outputPath, json);
    }

    private static string TransformWeatherId(string weather)
    {
        if (string.IsNullOrEmpty(weather)) return $"{IdTransformer.Namespace}:weather:outdoor/sunny";
        var name = weather.StartsWith("WEATHER_") ? weather[8..].ToLowerInvariant() : weather.ToLowerInvariant();
        return $"{IdTransformer.Namespace}:weather:outdoor/{name}";
    }

    private static string TransformBattleSceneId(string battleScene)
    {
        if (string.IsNullOrEmpty(battleScene)) return $"{IdTransformer.Namespace}:battlescene:normal/normal";
        var name = battleScene.StartsWith("MAP_BATTLE_SCENE_") ? battleScene[17..].ToLowerInvariant() : battleScene.ToLowerInvariant();
        return $"{IdTransformer.Namespace}:battlescene:normal/{name}";
    }

    private static string TransformBehaviorId(string movementType)
    {
        if (string.IsNullOrEmpty(movementType)) return $"{IdTransformer.Namespace}:behavior:npcs/stationary";
        var name = movementType.StartsWith("MOVEMENT_TYPE_") ? movementType[14..].ToLowerInvariant() : movementType.ToLowerInvariant();

        // Categorize movement types
        if (name.StartsWith("walk_sequence_")) return $"{IdTransformer.Namespace}:behavior:npcs/patrol";
        if (name.Contains("wander")) return $"{IdTransformer.Namespace}:behavior:npcs/wander";
        if (name.Contains("stationary") || name.StartsWith("face_") || name.Contains("look_around")) return $"{IdTransformer.Namespace}:behavior:npcs/stationary";
        if (name.Contains("walk") || name.Contains("pace")) return $"{IdTransformer.Namespace}:behavior:npcs/walk";
        if (name.Contains("jog") || name.Contains("run")) return $"{IdTransformer.Namespace}:behavior:npcs/jog";
        if (name.Contains("copy_player") || name.Contains("follow")) return $"{IdTransformer.Namespace}:behavior:npcs/follow";
        if (name.Contains("invisible")) return $"{IdTransformer.Namespace}:behavior:npcs/invisible";
        if (name.Contains("buried")) return $"{IdTransformer.Namespace}:behavior:npcs/buried";
        if (name.Contains("tree_disguise")) return $"{IdTransformer.Namespace}:behavior:npcs/disguise_tree";
        if (name.Contains("rock_disguise")) return $"{IdTransformer.Namespace}:behavior:npcs/disguise_rock";

        return $"{IdTransformer.Namespace}:behavior:npcs/{name}";
    }

    private static object? BuildBehaviorParameters(string movementType, int startX, int startY, int? rangeX, int? rangeY)
    {
        if (string.IsNullOrEmpty(movementType)) return null;
        var name = movementType.StartsWith("MOVEMENT_TYPE_") ? movementType[14..].ToLowerInvariant() : movementType.ToLowerInvariant();

        // Patrol behavior - calculate waypoint grid positions from direction sequence
        if (name.StartsWith("walk_sequence_"))
        {
            var waypoints = CalculatePatrolWaypoints(name, startX, startY, rangeX ?? 1, rangeY ?? 1);
            if (waypoints != null && waypoints.Length > 0)
            {
                return new { waypoints };
            }
            return null;
        }

        // Wander/Walk behaviors use range parameters
        if (name.Contains("wander") || name.Contains("walk") || name.Contains("pace"))
        {
            // Only include if range values are meaningful (non-zero)
            if ((rangeX.HasValue && rangeX.Value > 0) || (rangeY.HasValue && rangeY.Value > 0))
            {
                return new { rangeX = rangeX ?? 0, rangeY = rangeY ?? 0 };
            }
        }

        return null;
    }

    private static object[]? CalculatePatrolWaypoints(string name, int startX, int startY, int rangeX, int rangeY)
    {
        // Remove "walk_sequence_" prefix
        var sequence = name.StartsWith("walk_sequence_") ? name[14..] : name;

        // Parse the direction sequence (e.g., "up_right_left_down")
        var directions = new List<string>();
        var parts = sequence.Split('_');
        foreach (var part in parts)
        {
            if (part == "up" || part == "down" || part == "left" || part == "right")
            {
                directions.Add(part);
            }
        }

        if (directions.Count == 0) return null;

        // Calculate waypoints by following the direction sequence
        var waypoints = new List<object>();
        int currentX = startX;
        int currentY = startY;

        foreach (var dir in directions)
        {
            switch (dir)
            {
                case "up":
                    currentY -= rangeY * MetatileSize;
                    break;
                case "down":
                    currentY += rangeY * MetatileSize;
                    break;
                case "left":
                    currentX -= rangeX * MetatileSize;
                    break;
                case "right":
                    currentX += rangeX * MetatileSize;
                    break;
            }
            waypoints.Add(new { x = currentX, y = currentY });
        }

        return waypoints.ToArray();
    }

    private static string? ExtractDirection(string movementType)
    {
        if (string.IsNullOrEmpty(movementType)) return null;
        var lower = movementType.ToLowerInvariant();
        if (lower.Contains("_up") || lower.Contains("face_up")) return "up";
        if (lower.Contains("_down") || lower.Contains("face_down")) return "down";
        if (lower.Contains("_left") || lower.Contains("face_left")) return "left";
        if (lower.Contains("_right") || lower.Contains("face_right")) return "right";
        return null;
    }

    private static string? TransformTriggerId(string script)
    {
        if (string.IsNullOrEmpty(script) || script == "0x0" || script == "NULL") return null;
        return $"{IdTransformer.Namespace}:script:trigger/{IdTransformer.Normalize(script)}";
    }

    private static string? TransformInteractionId(string script)
    {
        if (string.IsNullOrEmpty(script) || script == "0x0" || script == "NULL" || script == "0") return null;
        return $"{IdTransformer.Namespace}:script:interaction/{IdTransformer.Normalize(script)}";
    }

    private object BuildConnections(List<Models.MapConnection> connections)
    {
        var result = new Dictionary<string, object>();
        foreach (var c in connections)
        {
            var dir = c.Direction.ToLowerInvariant();
            var key = dir switch
            {
                "up" => "north",
                "down" => "south",
                "left" => "west",
                "right" => "east",
                _ => dir
            };
            result[key] = new
            {
                mapId = IdTransformer.MapId(c.MapId, _region),
                offset = c.Offset
            };
        }
        return result;
    }

    private static string EncodeTileData(int[] data)
    {
        var bytes = new byte[data.Length * 4];
        for (int i = 0; i < data.Length; i++)
        {
            var tileBytes = BitConverter.GetBytes(data[i]);
            Buffer.BlockCopy(tileBytes, 0, bytes, i * 4, 4);
        }
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Encode uint tile data (preserves flip flags in high bits) to base64.
    /// </summary>
    private static string EncodeTileDataUint(uint[] data)
    {
        var bytes = new byte[data.Length * 4];
        for (int i = 0; i < data.Length; i++)
        {
            var tileBytes = BitConverter.GetBytes(data[i]);
            Buffer.BlockCopy(tileBytes, 0, bytes, i * 4, 4);
        }
        return Convert.ToBase64String(bytes);
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

            // Load palettes for animation processing
            var primaryPalettes = LoadPalettes(resolver, pair.PrimaryTileset);
            var secondaryPalettes = LoadPalettes(resolver, pair.SecondaryTileset);
            builder.ProcessAnimations(primaryPalettes, secondaryPalettes);
        }

        // Build and save individual tilesets (organized by primary/secondary)
        foreach (var result in _tilesetRegistry.BuildAllTilesets())
        {
            if (result.TileCount == 0) continue;

            SaveIndividualTilesheet(result);
            count++;
        }

        // Dispose the registry (cleans up all builders)
        _tilesetRegistry.Dispose();

        return count;
    }

    /// <summary>
    /// Save individual tilesheet image and JSON definition (organized by primary/secondary).
    /// </summary>
    private void SaveIndividualTilesheet(SharedTilesetResult result)
    {
        // Save to Graphics/Tilesets/{Primary|Secondary}/{name}.png
        var graphicsDir = Path.Combine(_outputPath, "Graphics", "Tilesets",
            result.TilesetType == "primary" ? "Primary" : "Secondary");
        Directory.CreateDirectory(graphicsDir);
        var imagePath = Path.Combine(graphicsDir, $"{result.TilesetName}.png");
        result.TilesheetImage.SaveAsPng(imagePath);

        // Save JSON to Definitions/Assets/Tilesets/{primary|secondary}/{name}.json
        var defsDir = Path.Combine(_outputPath, "Definitions", "Assets", "Tilesets", result.TilesetType);
        Directory.CreateDirectory(defsDir);

        // Build animation lookup by localTileId (use first animation if duplicates exist)
        var animationsByTile = result.Animations
            .GroupBy(a => a.LocalTileId)
            .ToDictionary(g => g.Key, g => g.First());

        // Build tiles array combining properties with animations
        object[]? tilesArray = null;
        if (result.TileProperties.Count > 0 || result.Animations.Count > 0)
        {
            var tiles = new List<object>();

            // Add all tile properties
            foreach (var prop in result.TileProperties)
            {
                // Check if this tile has an animation
                object? animation = null;
                if (animationsByTile.TryGetValue(prop.LocalTileId, out var anim))
                {
                    animation = anim.Frames.Select(f => new
                    {
                        tileId = f.TileId,
                        durationMs = f.DurationMs
                    });
                }

                tiles.Add(new
                {
                    localTileId = prop.LocalTileId,
                    behaviorId = prop.BehaviorId,
                    terrainId = prop.TerrainId,
                    collisionId = prop.CollisionId,
                    animation = animation
                });
            }

            // Add any animations that don't have properties (animation-only tiles)
            var propertyTileIds = result.TileProperties.Select(p => p.LocalTileId).ToHashSet();
            foreach (var anim in result.Animations.Where(a => !propertyTileIds.Contains(a.LocalTileId)))
            {
                tiles.Add(new
                {
                    localTileId = anim.LocalTileId,
                    behaviorId = (string?)null,
                    terrainId = (string?)null,
                    collisionId = (string?)null,
                    animation = anim.Frames.Select(f => new
                    {
                        tileId = f.TileId,
                        durationMs = f.DurationMs
                    })
                });
            }

            tilesArray = tiles.OrderBy(t => ((dynamic)t).localTileId).ToArray();
        }

        var texturePath = $"Graphics/Tilesets/{(result.TilesetType == "primary" ? "Primary" : "Secondary")}/{result.TilesetName}.png";
        var tilesetJson = new
        {
            id = result.TilesetId,
            name = result.TilesetName,
            type = result.TilesetType,
            texturePath = texturePath,
            tileWidth = MetatileSize,
            tileHeight = MetatileSize,
            tileCount = result.TileCount,
            columns = result.Columns,
            imageWidth = result.TilesheetImage.Width,
            imageHeight = result.TilesheetImage.Height,
            spacing = 0,
            margin = 0,
            tiles = tilesArray
        };

        var jsonPath = Path.Combine(defsDir, $"{result.TilesetName}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(tilesetJson, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(jsonPath, json);

        result.TilesheetImage.Dispose();
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
    /// Extract collision overrides from map binary data.
    /// Returns tiles where the collision override bit is set (non-zero = blocked).
    /// </summary>
    private static List<CollisionOverride> ExtractCollisionOverrides(ushort[] mapBin, int width, int height)
    {
        var overrides = new List<CollisionOverride>();

        for (int i = 0; i < mapBin.Length; i++)
        {
            var entry = mapBin[i];
            var collision = Infrastructure.MapBinReader.GetCollision(entry);

            // Only include tiles with non-zero collision (blocked)
            if (collision != 0)
            {
                var x = i % width;
                var y = i / width;
                var elevation = Infrastructure.MapBinReader.GetElevation(entry);

                overrides.Add(new CollisionOverride(
                    x * MetatileSize,
                    y * MetatileSize,
                    elevation
                ));
            }
        }

        return overrides;
    }

    /// <summary>
    /// Represents a per-tile collision override from map data.
    /// These tiles are blocked regardless of metatile behavior.
    /// </summary>
    private record CollisionOverride(int X, int Y, int Elevation);
}
