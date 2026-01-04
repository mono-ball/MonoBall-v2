using System.Text.Json;
using Porycon3.Models;
using Porycon3.Services.Interfaces;

namespace Porycon3.Infrastructure;

public class MapJsonReader : IMapReader
{
    private readonly string _pokeemeraldPath;
    private Dictionary<string, LayoutInfo>? _layoutsCache;

    public MapJsonReader(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
    }

    public MapData ReadMap(string mapName)
    {
        // Maps are in data/maps/{MAP_NAME}/map.json
        var mapPath = Path.Combine(_pokeemeraldPath, "data", "maps", mapName, "map.json");

        if (!File.Exists(mapPath))
            throw new FileNotFoundException($"Map not found: {mapPath}");

        var json = File.ReadAllText(mapPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Get layout ID from map.json
        var layoutId = root.TryGetProperty("layout", out var layout)
            ? layout.GetString() ?? ""
            : "";

        // Load layout info from global layouts.json
        var layoutInfo = GetLayoutInfo(layoutId);

        return new MapData
        {
            Id = root.TryGetProperty("id", out var id) ? id.GetString() ?? mapName : mapName,
            Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? mapName : mapName,
            Layout = new MapLayout
            {
                Id = layoutId,
                Width = layoutInfo?.Width ?? 0,
                Height = layoutInfo?.Height ?? 0,
                BorderWidth = layoutInfo?.BorderWidth ?? 2,
                BorderHeight = layoutInfo?.BorderHeight ?? 2,
                PrimaryTileset = layoutInfo?.PrimaryTileset ?? "",
                SecondaryTileset = layoutInfo?.SecondaryTileset ?? "",
                BlockdataPath = layoutInfo?.BlockdataPath ?? "",
                BorderPath = layoutInfo?.BorderPath ?? ""
            },
            Metadata = ParseMetadata(root),
            ObjectEvents = ParseObjectEvents(root),
            Warps = ParseWarps(root),
            CoordEvents = ParseCoordEvents(root),
            BgEvents = ParseBgEvents(root),
            Connections = ParseConnections(root)
        };
    }

    private LayoutInfo? GetLayoutInfo(string layoutId)
    {
        if (string.IsNullOrEmpty(layoutId))
            return null;

        // Load and cache layouts.json
        if (_layoutsCache == null)
        {
            _layoutsCache = LoadLayouts();
        }

        _layoutsCache.TryGetValue(layoutId, out var info);
        return info;
    }

    private Dictionary<string, LayoutInfo> LoadLayouts()
    {
        var layoutsPath = Path.Combine(_pokeemeraldPath, "data", "layouts", "layouts.json");
        var layouts = new Dictionary<string, LayoutInfo>();

        if (!File.Exists(layoutsPath))
            return layouts;

        var json = File.ReadAllText(layoutsPath);
        var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("layouts", out var layoutsArray))
        {
            foreach (var layoutEl in layoutsArray.EnumerateArray())
            {
                var id = layoutEl.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                if (string.IsNullOrEmpty(id))
                    continue;

                layouts[id] = new LayoutInfo
                {
                    Id = id,
                    Width = layoutEl.TryGetProperty("width", out var w) ? w.GetInt32() : 0,
                    Height = layoutEl.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
                    PrimaryTileset = layoutEl.TryGetProperty("primary_tileset", out var pt) ? pt.GetString() ?? "" : "",
                    SecondaryTileset = layoutEl.TryGetProperty("secondary_tileset", out var st) ? st.GetString() ?? "" : "",
                    BlockdataPath = layoutEl.TryGetProperty("blockdata_filepath", out var bp) ? bp.GetString() ?? "" : "",
                    BorderPath = layoutEl.TryGetProperty("border_filepath", out var bfp) ? bfp.GetString() ?? "" : "",
                    BorderWidth = layoutEl.TryGetProperty("border_width", out var bw) ? bw.GetInt32() : 2,
                    BorderHeight = layoutEl.TryGetProperty("border_height", out var bh) ? bh.GetInt32() : 2
                };
            }
        }

        return layouts;
    }

    private MapMetadata ParseMetadata(JsonElement root)
    {
        return new MapMetadata
        {
            Music = GetStringSafe(root, "music"),
            RegionMapSection = GetStringSafe(root, "region_map_section"),
            Weather = GetStringSafe(root, "weather"),
            MapType = GetStringSafe(root, "map_type"),
            BattleScene = GetStringSafe(root, "battle_scene"),
            RequiresFlash = GetBoolSafe(root, "requires_flash"),
            AllowCycling = GetBoolSafe(root, "allow_cycling"),
            AllowEscaping = GetBoolSafe(root, "allow_escaping"),
            AllowRunning = GetBoolSafe(root, "allow_running"),
            ShowMapName = GetBoolSafe(root, "show_map_name")
        };
    }

    private List<ObjectEvent> ParseObjectEvents(JsonElement root)
    {
        var events = new List<ObjectEvent>();

        if (root.TryGetProperty("object_events", out var objEvents) &&
            objEvents.ValueKind == JsonValueKind.Array)
        {
            foreach (var evt in objEvents.EnumerateArray())
            {
                events.Add(new ObjectEvent
                {
                    LocalId = evt.TryGetProperty("local_id", out var lid) ? lid.GetString() : null,
                    GraphicsId = GetStringSafe(evt, "graphics_id"),
                    X = GetIntSafe(evt, "x"),
                    Y = GetIntSafe(evt, "y"),
                    Elevation = GetIntSafe(evt, "elevation"),
                    MovementType = GetStringSafe(evt, "movement_type"),
                    MovementRangeX = GetIntSafe(evt, "movement_range_x"),
                    MovementRangeY = GetIntSafe(evt, "movement_range_y"),
                    TrainerType = GetStringSafe(evt, "trainer_type"),
                    TrainerSightOrBerryTreeId = GetStringSafe(evt, "trainer_sight_or_berry_tree_id"),
                    Script = GetStringSafe(evt, "script"),
                    Flag = GetStringSafe(evt, "flag")
                });
            }
        }

        return events;
    }

    private List<MapWarp> ParseWarps(JsonElement root)
    {
        var warps = new List<MapWarp>();
        if (root.TryGetProperty("warp_events", out var warpEvents) &&
            warpEvents.ValueKind == JsonValueKind.Array)
        {
            foreach (var warp in warpEvents.EnumerateArray())
            {
                warps.Add(new MapWarp
                {
                    X = GetIntSafe(warp, "x"),
                    Y = GetIntSafe(warp, "y"),
                    Elevation = GetIntSafe(warp, "elevation"),
                    DestMap = GetStringSafe(warp, "dest_map"),
                    DestWarpId = GetIntSafe(warp, "dest_warp_id")
                });
            }
        }
        return warps;
    }

    private List<CoordEvent> ParseCoordEvents(JsonElement root)
    {
        var events = new List<CoordEvent>();

        if (root.TryGetProperty("coord_events", out var coordEvents) &&
            coordEvents.ValueKind == JsonValueKind.Array)
        {
            foreach (var evt in coordEvents.EnumerateArray())
            {
                events.Add(new CoordEvent
                {
                    Type = GetStringSafe(evt, "type"),
                    X = GetIntSafe(evt, "x"),
                    Y = GetIntSafe(evt, "y"),
                    Elevation = GetIntSafe(evt, "elevation"),
                    Var = GetStringSafe(evt, "var"),
                    VarValue = GetStringSafe(evt, "var_value"),
                    Script = GetStringSafe(evt, "script")
                });
            }
        }

        return events;
    }

    private List<BgEvent> ParseBgEvents(JsonElement root)
    {
        var events = new List<BgEvent>();

        if (root.TryGetProperty("bg_events", out var bgEvents) &&
            bgEvents.ValueKind == JsonValueKind.Array)
        {
            foreach (var evt in bgEvents.EnumerateArray())
            {
                events.Add(new BgEvent
                {
                    Type = GetStringSafe(evt, "type"),
                    X = GetIntSafe(evt, "x"),
                    Y = GetIntSafe(evt, "y"),
                    Elevation = GetIntSafe(evt, "elevation"),
                    PlayerFacingDir = GetStringSafe(evt, "player_facing_dir"),
                    Script = GetStringSafe(evt, "script"),
                    Item = GetStringSafe(evt, "item"),
                    HiddenItemId = GetStringSafe(evt, "hidden_item_id")
                });
            }
        }

        return events;
    }

    private List<MapConnection> ParseConnections(JsonElement root)
    {
        var connections = new List<MapConnection>();
        if (root.TryGetProperty("connections", out var conns) &&
            conns.ValueKind == JsonValueKind.Array)
        {
            foreach (var conn in conns.EnumerateArray())
            {
                connections.Add(new MapConnection
                {
                    Direction = GetStringSafe(conn, "direction"),
                    Offset = GetIntSafe(conn, "offset"),
                    MapId = GetStringSafe(conn, "map")
                });
            }
        }
        return connections;
    }

    /// <summary>
    /// Safely get an integer from a JSON element that might be a string or number.
    /// </summary>
    private static int GetIntSafe(JsonElement el, string propName)
    {
        if (!el.TryGetProperty(propName, out var prop))
            return 0;

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.GetInt32(),
            JsonValueKind.String => int.TryParse(prop.GetString(), out var v) ? v : 0,
            _ => 0
        };
    }

    /// <summary>
    /// Safely get a string from a JSON element.
    /// </summary>
    private static string GetStringSafe(JsonElement el, string propName)
    {
        if (!el.TryGetProperty(propName, out var prop))
            return "";

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString() ?? "",
            JsonValueKind.Number => prop.ToString(),
            _ => ""
        };
    }

    /// <summary>
    /// Safely get a boolean from a JSON element.
    /// </summary>
    private static bool GetBoolSafe(JsonElement el, string propName)
    {
        if (!el.TryGetProperty(propName, out var prop))
            return false;

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(prop.GetString(), out var v) && v,
            _ => false
        };
    }

    private class LayoutInfo
    {
        public string Id { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public string PrimaryTileset { get; set; } = "";
        public string SecondaryTileset { get; set; } = "";
        public string BlockdataPath { get; set; } = "";
        public string BorderPath { get; set; } = "";
        public int BorderWidth { get; set; } = 2;
        public int BorderHeight { get; set; } = 2;
    }
}
