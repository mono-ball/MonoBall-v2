using Porycon3.Models;
using Porycon3.Infrastructure;
using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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

            // 4. Build per-map tilesheet and layer data using rendered metatiles
            using var tilesheetBuilder = new MapTilesheetBuilder(_inputPath);

            var (layers, tileCount) = ProcessMapWithMetatiles(
                mapBin,
                primaryMetatiles,
                secondaryMetatiles,
                mapData.Layout.PrimaryTileset,
                mapData.Layout.SecondaryTileset,
                mapData.Layout.Width,
                mapData.Layout.Height,
                tilesheetBuilder);

            // 4.5. Process animations - load palettes and add animation frames
            var resolver = new TilesetPathResolver(_inputPath);
            var primaryPalettes = LoadPalettes(resolver, mapData.Layout.PrimaryTileset);
            var secondaryPalettes = LoadPalettes(resolver, mapData.Layout.SecondaryTileset);
            tilesheetBuilder.ProcessAnimations(
                mapData.Layout.PrimaryTileset,
                mapData.Layout.SecondaryTileset,
                primaryPalettes,
                secondaryPalettes);

            // 5. Build and save tilesheet
            using var tilesheetImage = tilesheetBuilder.BuildTilesheetImage();
            var animations = tilesheetBuilder.GetAnimations();
            SaveTilesheet(mapName, tilesheetImage, tilesheetBuilder.TileCount, animations);

            // 6. Write map output
            WriteOutput(mapName, mapData, layers);

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
    /// Process map using rendered 16x16 metatile images.
    /// Creates layer data with GIDs referencing the per-map tilesheet.
    /// Also extracts elevation data for each tile position.
    /// </summary>
    private (List<LayerData> Layers, int TileCount) ProcessMapWithMetatiles(
        ushort[] mapBin,
        List<Metatile> primaryMetatiles,
        List<Metatile> secondaryMetatiles,
        string primaryTileset,
        string secondaryTileset,
        int width,
        int height,
        MapTilesheetBuilder builder)
    {
        // Combine metatiles (primary 0-511, secondary 512+)
        var allMetatiles = primaryMetatiles.Concat(secondaryMetatiles).ToList();

        // Layer data: each cell is one metatile (16x16), not individual tiles
        var bg3Data = new int[width * height];
        var bg2Data = new int[width * height];
        var bg1Data = new int[width * height];

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
                var actualMetatileId = isSecondaryMetatile
                    ? metatileId - primaryMetatiles.Count
                    : metatileId;

                // Render metatile and get GIDs
                var (bottomGid, topGid) = builder.ProcessMetatile(
                    metatile,
                    actualMetatileId,
                    metatileTileset,
                    primaryTileset,
                    secondaryTileset);

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
        var layers = new List<LayerData>
        {
            new() { Name = "Ground", Width = width, Height = height, Data = bg3Data, Elevation = 0 },
            new() { Name = "Objects", Width = width, Height = height, Data = bg2Data, Elevation = 3 },
            new() { Name = "Overhead", Width = width, Height = height, Data = bg1Data, Elevation = 15 }
        };

        return (layers, builder.TileCount);
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
    /// Save tilesheet image and JSON definition.
    /// </summary>
    private void SaveTilesheet(string mapName, Image<Rgba32> image, int tileCount, List<TileAnimation> animations)
    {
        var normalizedName = IdTransformer.Normalize(mapName);

        // Format region name properly (e.g., "hoenn" -> "Hoenn")
        var regionFormatted = _region.ToUpperInvariant()[0] + _region[1..].ToLowerInvariant();

        // Save to Graphics/Tilesets/{Region}/ for image (filename matches map)
        var graphicsDir = Path.Combine(_outputPath, "Graphics", "Tilesets", regionFormatted);
        Directory.CreateDirectory(graphicsDir);
        var imagePath = Path.Combine(graphicsDir, $"{mapName}.png");
        image.SaveAsPng(imagePath);

        // Save JSON to Definitions/Assets/Tilesets/{region}/ (filename matches map)
        var defsDir = Path.Combine(_outputPath, "Definitions", "Assets", "Tilesets", _region);
        Directory.CreateDirectory(defsDir);

        var cols = Math.Min(TilesPerRow, Math.Max(1, tileCount));

        // Build tiles array with animations
        object[]? tilesArray = null;
        if (animations.Count > 0)
        {
            tilesArray = animations.Select(a => (object)new
            {
                localTileId = a.LocalTileId,
                type = (string?)null,
                tileBehaviorId = (string?)null,
                animation = a.Frames.Select(f => new
                {
                    tileId = f.TileId,
                    durationMs = f.DurationMs
                })
            }).ToArray();
        }

        var tilesetJson = new
        {
            id = $"base:tileset:{_region}/{normalizedName}",
            name = mapName,
            texturePath = $"Graphics/Tilesets/{regionFormatted}/{mapName}.png",
            tileWidth = MetatileSize,
            tileHeight = MetatileSize,
            tileCount = tileCount,
            columns = cols,
            imageWidth = image.Width,
            imageHeight = image.Height,
            spacing = 0,
            margin = 0,
            tiles = tilesArray
        };

        var jsonPath = Path.Combine(defsDir, $"{mapName}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(tilesetJson, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(jsonPath, json);
    }

    private void WriteOutput(string mapName, MapData mapData, List<LayerData> layers)
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
            regionId = $"base:region:{_region}",
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
                id = $"base:layer:{_region}/{normalizedName}/{l.Name.ToLowerInvariant()}",
                name = l.Name,
                type = "tilelayer",
                width = l.Width,
                height = l.Height,
                elevation = l.Elevation,
                visible = true,
                opacity = 1,
                offsetX = 0,
                offsetY = 0,
                tileData = EncodeTileData(l.Data),
                imagePath = (string?)null
            }),
            tilesets = new[]
            {
                new { firstGid = 1, tilesetId = $"base:tileset:{_region}/{normalizedName}" }
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
                    id = $"base:warp:{_region}/{normalizedName}/warp_to_{destNormalized}",
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
                    id = $"base:trigger:{_region}/{normalizedName}/trigger_{varNormalized}_{value}",
                    name = $"Trigger: {c.Var} == {c.VarValue}",
                    x = c.X * MetatileSize,
                    y = c.Y * MetatileSize,
                    width = MetatileSize,
                    height = MetatileSize,
                    variable = $"base:variable:{_region}/{c.Var.ToLowerInvariant()}",
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
                    id = $"base:interaction:{_region}/{normalizedName}/{b.Type.ToLowerInvariant()}_{scriptNormalized}",
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
                id = $"base:npc:{_region}/{normalizedName}/{(o.LocalId ?? $"npc_{idx}").ToLowerInvariant()}",
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
            })
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
        if (string.IsNullOrEmpty(weather)) return "base:weather:outdoor/sunny";
        var name = weather.StartsWith("WEATHER_") ? weather[8..].ToLowerInvariant() : weather.ToLowerInvariant();
        return $"base:weather:outdoor/{name}";
    }

    private static string TransformBattleSceneId(string battleScene)
    {
        if (string.IsNullOrEmpty(battleScene)) return "base:battlescene:normal/normal";
        var name = battleScene.StartsWith("MAP_BATTLE_SCENE_") ? battleScene[17..].ToLowerInvariant() : battleScene.ToLowerInvariant();
        return $"base:battlescene:normal/{name}";
    }

    private static string TransformBehaviorId(string movementType)
    {
        if (string.IsNullOrEmpty(movementType)) return "base:behavior:stationary";
        var name = movementType.StartsWith("MOVEMENT_TYPE_") ? movementType[14..].ToLowerInvariant() : movementType.ToLowerInvariant();

        // Categorize movement types
        if (name.StartsWith("walk_sequence_")) return "base:behavior:patrol";
        if (name.Contains("wander")) return "base:behavior:wander";
        if (name.Contains("stationary") || name.StartsWith("face_") || name.Contains("look_around")) return "base:behavior:stationary";
        if (name.Contains("walk") || name.Contains("pace")) return "base:behavior:walk";
        if (name.Contains("jog") || name.Contains("run")) return "base:behavior:jog";
        if (name.Contains("copy_player") || name.Contains("follow")) return "base:behavior:follow";
        if (name.Contains("invisible")) return "base:behavior:invisible";
        if (name.Contains("buried")) return "base:behavior:buried";
        if (name.Contains("tree_disguise")) return "base:behavior:disguise_tree";
        if (name.Contains("rock_disguise")) return "base:behavior:disguise_rock";

        return $"base:behavior:{name}";
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
        return $"base:script:trigger/{IdTransformer.Normalize(script)}";
    }

    private static string? TransformInteractionId(string script)
    {
        if (string.IsNullOrEmpty(script) || script == "0x0" || script == "NULL" || script == "0") return null;
        return $"base:script:interaction/{IdTransformer.Normalize(script)}";
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
    /// Generate additional definitions (Weather, BattleScenes, Region, Sprites, Pokemon, Species) based on IDs
    /// referenced by converted maps. Call this after all maps have been converted.
    /// </summary>
    public (int Weather, int BattleScenes, bool Region, int Sections, int Themes, int PopupBackgrounds, int PopupOutlines, int WeatherGraphics, int BattleEnvironments, int Sprites, int TextWindows, int Pokemon, int PokemonSprites, int Species, int SpeciesForms, int FieldEffects, int DoorAnimations) GenerateDefinitions()
    {
        var (weather, battleScenes, region) = _definitionGenerator.GenerateAll();
        var (sections, themes) = _sectionExtractor.ExtractAll();
        var (popupBackgrounds, popupOutlines) = _popupExtractor.ExtractAll();
        var (weatherGraphics, _) = _weatherExtractor.ExtractAll();
        var (battleEnvs, _) = _battleEnvExtractor.ExtractAll();
        var (sprites, _) = _spriteExtractor.ExtractAll();
        var textWindows = _textWindowExtractor.ExtractAll();
        var (pokemon, pokemonSprites, _) = _pokemonExtractor.ExtractAll();
        var (species, speciesForms) = _speciesExtractor.ExtractAll();
        var fieldEffects = _fieldEffectExtractor.ExtractAll();
        var doorAnims = _doorAnimExtractor.Extract();
        return (weather, battleScenes, region, sections, themes, popupBackgrounds, popupOutlines, weatherGraphics, battleEnvs, sprites, textWindows, pokemon, pokemonSprites, species, speciesForms, fieldEffects, doorAnims);
    }
}
