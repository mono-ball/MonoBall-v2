using Porycon3.Models;
using Porycon3.Infrastructure;
using System.Diagnostics;

namespace Porycon3.Services;

public class MapConversionService
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly string _region;
    private readonly bool _verbose;

    private readonly MapJsonReader _mapReader;
    private readonly MetatileBinReader _metatileReader;
    private readonly MapBinReader _mapBinReader;
    private readonly MetatileProcessor _metatileProcessor;
    private readonly TilesetBuilder _tilesetBuilder;

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
        _metatileProcessor = new MetatileProcessor();
        _tilesetBuilder = new TilesetBuilder();
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
            var allMetatiles = primaryMetatiles.Concat(secondaryMetatiles).ToList();

            // 3. Read map binary (metatile indices)
            var mapBin = _mapBinReader.ReadMapBin(
                mapData.Layout.Id,
                mapData.Layout.Width,
                mapData.Layout.Height,
                mapData.Layout.BlockdataPath);

            // 4. Process into layers
            var layers = _metatileProcessor.ProcessMap(mapBin, allMetatiles,
                mapData.Layout.Width, mapData.Layout.Height);

            // 5. Write output
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

    private void WriteOutput(string mapName, MapData mapData, List<LayerData> layers)
    {
        var outputDir = Path.Combine(_outputPath, _region);
        Directory.CreateDirectory(outputDir);

        var normalizedName = IdTransformer.Normalize(mapName);
        var outputPath = Path.Combine(outputDir, $"{mapName}.json");

        var output = new
        {
            id = IdTransformer.MapIdFromName(mapName, _region),
            name = mapData.Name,
            description = "",
            regionId = $"base:region:{_region}",
            mapType = mapData.Metadata.MapType,
            width = mapData.Layout.Width,
            height = mapData.Layout.Height,
            tileWidth = 16,
            tileHeight = 16,
            musicId = IdTransformer.AudioId(mapData.Metadata.Music),
            weatherId = TransformWeatherId(mapData.Metadata.Weather),
            battleSceneId = TransformBattleSceneId(mapData.Metadata.BattleScene),
            mapSectionId = IdTransformer.MapsecId(mapData.Metadata.RegionMapSection, _region),
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
                visible = true,
                opacity = 1,
                offsetX = 0,
                offsetY = 0,
                tileData = EncodeTileData(l.Data),
                imagePath = (string?)null
            }),
            tilesetRefs = new[]
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
                    x = w.X * 16,
                    y = w.Y * 16,
                    width = 16,
                    height = 16,
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
                    x = c.X * 16,
                    y = c.Y * 16,
                    width = 16,
                    height = 16,
                    variable = $"base:variable:{_region}/{c.Var}",
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
                    x = b.X * 16,
                    y = b.Y * 16,
                    width = 16,
                    height = 16,
                    interactionId = TransformInteractionId(b.Script),
                    elevation = b.Elevation
                };
            }),
            npcs = mapData.ObjectEvents.Select((o, idx) => new
            {
                id = $"base:npc:{_region}/{normalizedName}/{(o.LocalId ?? $"npc_{idx}").ToLowerInvariant()}",
                name = o.LocalId ?? $"NPC_{idx}",
                x = o.X * 16,
                y = o.Y * 16,
                spriteId = IdTransformer.SpriteId(o.GraphicsId),
                behaviorId = TransformBehaviorId(o.MovementType),
                interactionId = TransformInteractionId(o.Script),
                visibilityFlag = string.IsNullOrEmpty(o.Flag) || o.Flag == "0" ? null : IdTransformer.FlagId(o.Flag),
                direction = ExtractDirection(o.MovementType),
                rangeX = o.MovementRangeX,
                rangeY = o.MovementRangeY,
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
        if (string.IsNullOrEmpty(movementType)) return "base:script:behavior/stationary";
        var name = movementType.StartsWith("MOVEMENT_TYPE_") ? movementType[14..].ToLowerInvariant() : movementType.ToLowerInvariant();
        // Simplify movement names
        if (name.Contains("wander")) return "base:script:behavior/wander";
        if (name.Contains("stationary") || name.Contains("face_") || name.Contains("look_around")) return "base:script:behavior/stationary";
        if (name.Contains("walk")) return "base:script:behavior/walk";
        if (name.Contains("jog")) return "base:script:behavior/jog";
        return $"base:script:behavior/{name}";
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
}
