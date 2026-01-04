using Porycon3.Models;
using Porycon3.Services;
using static Porycon3.Infrastructure.TileConstants;

// Use the record from MapConversionService
using ResolvedBorderData = Porycon3.Services.ResolvedBorderData;

namespace Porycon3.Services.Builders;

/// <summary>
/// Builds map JSON output structure.
/// </summary>
public class MapOutputBuilder
{
    private readonly string _region;

    public MapOutputBuilder(string region)
    {
        _region = region;
    }

    public object BuildMapOutput(
        string mapName,
        MapData mapData,
        List<SharedLayerData> layers,
        TilesetPairKey tilesetPair,
        int primaryTileCount,
        string primaryTilesetType,
        string secondaryTilesetType,
        List<CollisionLayerData> collisionLayers,
        string? weatherId,
        string? battleSceneId,
        ResolvedBorderData? borderData = null)
    {
        var normalizedName = IdTransformer.Normalize(mapName);

        return new
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
            border = BuildBorder(borderData, tilesetPair, primaryTileCount, primaryTilesetType, secondaryTilesetType),
            layers = BuildLayers(layers, normalizedName),
            tilesets = new[]
            {
                new { firstGid = 1, tilesetId = IdTransformer.TilesetId(tilesetPair.PrimaryTileset, primaryTilesetType) },
                new { firstGid = primaryTileCount + 1, tilesetId = IdTransformer.TilesetId(tilesetPair.SecondaryTileset, secondaryTilesetType) }
            },
            warps = BuildWarps(mapData.Warps, normalizedName),
            triggers = BuildTriggers(mapData.CoordEvents, normalizedName),
            interactions = BuildInteractions(mapData.BgEvents, normalizedName),
            npcs = BuildNpcs(mapData.ObjectEvents, normalizedName),
            collisions = BuildCollisions(collisionLayers, normalizedName)
        };
    }

    private IEnumerable<object> BuildLayers(List<SharedLayerData> layers, string normalizedName)
    {
        return layers.Select(l => new
        {
            id = $"{IdTransformer.Namespace}:layer:{_region}/{normalizedName}/{l.Name.ToLowerInvariant()}",
            name = l.Name,
            width = l.Width,
            height = l.Height,
            elevation = l.Elevation,
            visible = true,
            opacity = 1,
            offsetX = 0,
            offsetY = 0,
            tileData = EncodeTileDataUint(l.Data)
        });
    }

    private IEnumerable<object> BuildWarps(List<MapWarp> warps, string normalizedName)
    {
        return warps.Select((w, idx) =>
        {
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
        });
    }

    private IEnumerable<object> BuildTriggers(List<CoordEvent> events, string normalizedName)
    {
        return events.Select((c, idx) =>
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
        });
    }

    private IEnumerable<object> BuildInteractions(List<BgEvent> events, string normalizedName)
    {
        return events.Select((b, idx) =>
        {
            var scriptNormalized = IdTransformer.Normalize(b.Script).Replace("_", "");
            var typeDisplay = char.ToUpper(b.Type[0]) + b.Type[1..].ToLowerInvariant();
            // Determine script category based on BgEvent type (signs vs npcs)
            var scriptCategory = b.Type.Equals("sign", StringComparison.OrdinalIgnoreCase) ? "signs" : "npcs";
            return new
            {
                id = $"{IdTransformer.Namespace}:interaction:{_region}/{normalizedName}/{b.Type.ToLowerInvariant()}_{scriptNormalized}",
                name = $"{typeDisplay}: {b.Script}",
                x = b.X * MetatileSize,
                y = b.Y * MetatileSize,
                width = MetatileSize,
                height = MetatileSize,
                interactionId = TransformInteractionId(b.Script, scriptCategory),
                elevation = b.Elevation
            };
        });
    }

    private IEnumerable<object> BuildNpcs(List<ObjectEvent> objects, string normalizedName)
    {
        return objects.Select((o, idx) => new
        {
            id = $"{IdTransformer.Namespace}:npc:{_region}/{normalizedName}/{(o.LocalId ?? $"npc_{idx}").ToLowerInvariant()}",
            name = o.LocalId ?? $"NPC_{idx}",
            x = o.X * MetatileSize,
            y = o.Y * MetatileSize,
            spriteId = IdTransformer.SpriteId(o.GraphicsId),
            behaviorId = BehaviorTransformer.TransformBehaviorId(o.MovementType),
            behaviorParameters = BehaviorTransformer.BuildBehaviorParameters(o.MovementType, o.X * MetatileSize, o.Y * MetatileSize, o.MovementRangeX, o.MovementRangeY),
            interactionId = TransformInteractionId(o.Script, "npcs"),
            visibilityFlag = string.IsNullOrEmpty(o.Flag) || o.Flag == "0" ? null : IdTransformer.FlagId(o.Flag),
            elevation = o.Elevation
        });
    }

    private IEnumerable<object> BuildCollisions(List<CollisionLayerData> layers, string normalizedName)
    {
        return layers.Select(c => new
        {
            id = $"{IdTransformer.Namespace}:collision:{_region}/{normalizedName}/elevation_{c.Elevation}",
            name = $"Collision_{c.Elevation}",
            width = c.Width,
            height = c.Height,
            elevation = c.Elevation,
            offsetX = 0,
            offsetY = 0,
            tileData = EncodeCollisionData(c.Data)
        });
    }

    private object BuildConnections(List<MapConnection> connections)
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

    /// <summary>
    /// Transforms a script name to a trigger script definition ID.
    /// Uses IdTransformer.TriggerScriptId to ensure consistency with definition files.
    /// </summary>
    /// <param name="script">The script name (e.g., "LittlerootTown_EventScript_NeedPokemonTriggerLeft").</param>
    /// <returns>The script definition ID, or null if script is invalid.</returns>
    private static string? TransformTriggerId(string script)
    {
        var id = IdTransformer.TriggerScriptId(script);
        return string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>
    /// Transforms a script name to an interaction script definition ID.
    /// Uses IdTransformer.InteractionScriptId to ensure consistency with definition files.
    /// </summary>
    /// <param name="script">The script name (e.g., "LittlerootTown_EventScript_TownSign").</param>
    /// <param name="category">The interaction category: "npcs" or "signs".</param>
    /// <returns>The script definition ID, or null if script is invalid.</returns>
    private static string? TransformInteractionId(string script, string category = "npcs")
    {
        var id = IdTransformer.InteractionScriptId(script, category);
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static object? BuildBorder(
        ResolvedBorderData? borderData,
        TilesetPairKey tilesetPair,
        int primaryTileCount,
        string primaryTilesetType,
        string secondaryTilesetType)
    {
        if (borderData == null)
            return null;

        // Determine which tileset to use based on whether GIDs reference primary or secondary
        // Border tiles typically all come from the same tileset
        // Check first GID to determine which tileset
        var firstGid = borderData.BottomLayerGids.FirstOrDefault();
        var isSecondary = firstGid > primaryTileCount;
        var tilesetName = isSecondary ? tilesetPair.SecondaryTileset : tilesetPair.PrimaryTileset;
        var tilesetType = isSecondary ? secondaryTilesetType : primaryTilesetType;

        return new
        {
            tilesetId = IdTransformer.TilesetId(tilesetName, tilesetType),
            bottomLayer = borderData.BottomLayerGids.ToList(),
            topLayer = borderData.TopLayerGids.ToList()
        };
    }

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

    private static string EncodeCollisionData(byte[] data) => Convert.ToBase64String(data);
}

/// <summary>
/// Collision layer data for a specific elevation.
/// </summary>
public record CollisionLayerData(int Width, int Height, int Elevation, byte[] Data);
