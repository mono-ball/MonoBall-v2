using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Maps
{
    /// <summary>
    /// Represents a map definition loaded from JSON.
    /// </summary>
    public class MapDefinition
    {
        /// <summary>
        /// The unique identifier for the map.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The name of the map.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The description of the map.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// The region ID this map belongs to.
        /// </summary>
        [JsonPropertyName("regionId")]
        public string? RegionId { get; set; }

        /// <summary>
        /// The type of map.
        /// </summary>
        [JsonPropertyName("mapType")]
        public string? MapType { get; set; }

        /// <summary>
        /// The width of the map in tiles.
        /// </summary>
        [JsonPropertyName("width")]
        public int Width { get; set; }

        /// <summary>
        /// The height of the map in tiles.
        /// </summary>
        [JsonPropertyName("height")]
        public int Height { get; set; }

        /// <summary>
        /// The width of each tile in pixels.
        /// </summary>
        [JsonPropertyName("tileWidth")]
        public int TileWidth { get; set; }

        /// <summary>
        /// The height of each tile in pixels.
        /// </summary>
        [JsonPropertyName("tileHeight")]
        public int TileHeight { get; set; }

        /// <summary>
        /// The music ID to play on this map.
        /// </summary>
        [JsonPropertyName("musicId")]
        public string? MusicId { get; set; }

        /// <summary>
        /// The weather ID for this map.
        /// </summary>
        [JsonPropertyName("weatherId")]
        public string? WeatherId { get; set; }

        /// <summary>
        /// The battle scene ID for this map.
        /// </summary>
        [JsonPropertyName("battleSceneId")]
        public string? BattleSceneId { get; set; }

        /// <summary>
        /// The map section ID.
        /// </summary>
        [JsonPropertyName("mapSectionId")]
        public string? MapSectionId { get; set; }

        /// <summary>
        /// Whether to show the map name.
        /// </summary>
        [JsonPropertyName("showMapName")]
        public bool ShowMapName { get; set; }

        /// <summary>
        /// Whether the player can fly from this map.
        /// </summary>
        [JsonPropertyName("canFly")]
        public bool CanFly { get; set; }

        /// <summary>
        /// Whether this map requires flash.
        /// </summary>
        [JsonPropertyName("requiresFlash")]
        public bool RequiresFlash { get; set; }

        /// <summary>
        /// Whether running is allowed.
        /// </summary>
        [JsonPropertyName("allowRunning")]
        public bool AllowRunning { get; set; }

        /// <summary>
        /// Whether cycling is allowed.
        /// </summary>
        [JsonPropertyName("allowCycling")]
        public bool AllowCycling { get; set; }

        /// <summary>
        /// Whether escaping is allowed.
        /// </summary>
        [JsonPropertyName("allowEscaping")]
        public bool AllowEscaping { get; set; }

        /// <summary>
        /// The connections to other maps.
        /// </summary>
        [JsonPropertyName("connections")]
        public Dictionary<string, MapConnection>? Connections { get; set; }

        /// <summary>
        /// The border tile configuration.
        /// </summary>
        [JsonPropertyName("border")]
        public MapBorder? Border { get; set; }

        /// <summary>
        /// The layers of the map.
        /// </summary>
        [JsonPropertyName("layers")]
        public List<MapLayer> Layers { get; set; } = new List<MapLayer>();

        /// <summary>
        /// The tileset references.
        /// </summary>
        [JsonPropertyName("tilesetRefs")]
        public List<TilesetReference> TilesetRefs { get; set; } = new List<TilesetReference>();

        /// <summary>
        /// The NPCs on this map.
        /// </summary>
        [JsonPropertyName("npcs")]
        public List<NpcDefinition>? Npcs { get; set; }
    }

    /// <summary>
    /// Represents an NPC definition in a map.
    /// </summary>
    public class NpcDefinition
    {
        /// <summary>
        /// The unique identifier for the NPC.
        /// </summary>
        [JsonPropertyName("npcId")]
        public string NpcId { get; set; } = string.Empty;

        /// <summary>
        /// The name of the NPC.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The X position of the NPC in tiles.
        /// </summary>
        [JsonPropertyName("x")]
        public int X { get; set; }

        /// <summary>
        /// The Y position of the NPC in tiles.
        /// </summary>
        [JsonPropertyName("y")]
        public int Y { get; set; }

        /// <summary>
        /// The sprite ID for this NPC.
        /// </summary>
        [JsonPropertyName("spriteId")]
        public string SpriteId { get; set; } = string.Empty;

        /// <summary>
        /// The behavior ID for this NPC.
        /// </summary>
        [JsonPropertyName("behaviorId")]
        public string? BehaviorId { get; set; }

        /// <summary>
        /// The interaction script ID for this NPC.
        /// </summary>
        [JsonPropertyName("interactionScript")]
        public string? InteractionScript { get; set; }

        /// <summary>
        /// The visibility flag for this NPC (null if always visible).
        /// </summary>
        [JsonPropertyName("visibilityFlag")]
        public string? VisibilityFlag { get; set; }

        /// <summary>
        /// The initial facing direction of the NPC (null, "up", "down", "left", "right").
        /// </summary>
        [JsonPropertyName("direction")]
        public string? Direction { get; set; }

        /// <summary>
        /// The X range for movement behavior.
        /// </summary>
        [JsonPropertyName("rangeX")]
        public int RangeX { get; set; }

        /// <summary>
        /// The Y range for movement behavior.
        /// </summary>
        [JsonPropertyName("rangeY")]
        public int RangeY { get; set; }

        /// <summary>
        /// The elevation (z-order) of the NPC.
        /// </summary>
        [JsonPropertyName("elevation")]
        public int Elevation { get; set; }
    }

    /// <summary>
    /// Represents the border configuration of a map.
    /// </summary>
    public class MapBorder
    {
        /// <summary>
        /// The tileset ID for the border.
        /// </summary>
        [JsonPropertyName("tilesetId")]
        public string TilesetId { get; set; } = string.Empty;

        /// <summary>
        /// The bottom layer tile IDs.
        /// </summary>
        [JsonPropertyName("bottomLayer")]
        public List<int> BottomLayer { get; set; } = new List<int>();

        /// <summary>
        /// The top layer tile IDs.
        /// </summary>
        [JsonPropertyName("topLayer")]
        public List<int> TopLayer { get; set; } = new List<int>();
    }
}
