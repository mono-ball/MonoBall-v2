namespace Porycon3.Services;

/// <summary>
/// Tile-related ID transformations (interactions, terrain, collision, tilesets).
/// </summary>
public static partial class IdTransformer
{
    #region Tile Interaction IDs

    /// <summary>
    /// Transform metatile behavior value to tile interaction ID.
    /// 0 (MB_NORMAL) -> null (default, not stored)
    /// Other values -> base:interaction/tiles/{name}
    /// </summary>
    public static string? TileInteractionId(int behaviorValue)
    {
        var pureBehavior = behaviorValue & 0xFF;
        if (pureBehavior == 0)
            return null;

        var name = GetMetatileBehaviorName(pureBehavior);
        return $"{Namespace}:interaction/tiles/{name}";
    }

    private static string GetMetatileBehaviorName(int behavior)
    {
        return behavior switch
        {
            0x00 => "normal",
            0x01 => "tall_grass",
            0x02 => "very_tall_grass",
            0x03 => "underwater_grass",
            0x04 => "shore_water",
            0x05 => "deep_water",
            0x06 => "waterfall",
            0x07 => "ocean_water",
            0x08 => "pond_water",
            0x09 => "puddle",
            0x0A => "no_running",
            0x0B => "indoor_encounter",
            0x0C => "mountain",
            0x0D => "secret_base_hole",
            0x0E => "footprints",
            0x0F => "thin_ice",
            0x10 => "cracked_ice",
            0x11 => "hot_spring",
            0x12 => "lava",
            0x13 => "sand",
            0x14 => "ash_grass",
            0x15 => "sand_cave",
            0x16 => "ledge_south",
            0x17 => "ledge_north",
            0x18 => "ledge_east",
            0x19 => "ledge_west",
            0x1A => "ledge_southeast",
            0x1B => "ledge_southwest",
            0x1C => "ledge_northeast",
            0x1D => "ledge_northwest",
            0x1E => "stairs_south",
            0x1F => "stairs_north",
            0x38 => "impassable_south",
            0x39 => "impassable_north",
            0x3A => "impassable_east",
            0x3B => "impassable_west",
            0x3C => "cycling_road_pull_south",
            0x3D => "cycling_road_pull_east",
            0x40 => "bump",
            0x41 => "walk_south",
            0x42 => "walk_north",
            0x43 => "walk_east",
            0x44 => "walk_west",
            0x45 => "slide_south",
            0x46 => "slide_north",
            0x47 => "slide_east",
            0x48 => "slide_west",
            0x49 => "trick_house_puzzle_8_floor",
            0x4A => "muddy_slope",
            0x60 => "spin_right",
            0x61 => "spin_left",
            0x62 => "spin_down",
            0x63 => "spin_up",
            0x64 => "ice_spin_right",
            0x65 => "ice_spin_left",
            0x66 => "ice_spin_down",
            0x67 => "ice_spin_up",
            0x68 => "secret_base_rock_wall",
            0x69 => "secret_base_shrub",
            0x80 => "warp_or_bridge",
            0x81 => "deep_water_2",
            0x82 => "warp_door",
            0x83 => "warp_or_bridge_2",
            0x8A => "pokecenter_sign",
            0x8B => "pokemart_sign",
            0x8C => "indigo_plateau_mark_1",
            0x8D => "indigo_plateau_mark_2",
            0x8E => "indigo_plateau_mark_3",
            0x8F => "indigo_plateau_mark_4",
            0x90 => "berry_tree_soil",
            0x91 => "secret_base_pc",
            0x92 => "secret_base_bed_foot",
            0xA0 => "cable_car_climb",
            0xA1 => "cable_car_descend",
            0xC0 => "counter",
            0xD0 => "player_face_down",
            0xD1 => "player_face_up",
            0xD2 => "player_face_left",
            0xD3 => "player_face_right",
            0xE0 => "trainer_see_north",
            0xE1 => "trainer_see_south",
            0xE2 => "trainer_see_west",
            0xE3 => "trainer_see_east",
            0xE4 => "trainer_see_all_directions",
            _ => $"unknown_{behavior:x2}"
        };
    }

    #endregion

    #region Terrain Type IDs

    /// <summary>
    /// Transform terrain type value to terrain ID.
    /// 0 (TERRAIN_NORMAL) -> null (default, not stored)
    /// Other values -> base:terrain:{name}
    /// </summary>
    public static string? TerrainTypeId(int terrainValue)
    {
        if (terrainValue == 0)
            return null;

        var name = GetTerrainTypeName(terrainValue);
        return $"{Namespace}:terrain:{name}";
    }

    private static string GetTerrainTypeName(int terrain)
    {
        return terrain switch
        {
            0 => "normal",
            1 => "grass",
            2 => "water",
            3 => "waterfall",
            4 => "deep_water",
            5 => "pond_water",
            6 => "sand",
            7 => "mountain",
            _ => $"unknown_{terrain}"
        };
    }

    #endregion

    #region Collision IDs

    /// <summary>
    /// Derive collision ID from behavior value.
    /// Returns null for "passable" (default), otherwise "base:collision:{type}".
    /// </summary>
    public static string? DeriveCollisionId(int behavior)
    {
        var collisionType = GetCollisionType(behavior);

        if (collisionType == "passable")
            return null;

        return $"{Namespace}:collision:{collisionType}";
    }

    private static string GetCollisionType(int behavior)
    {
        var pureBehavior = behavior & 0xFF;

        return pureBehavior switch
        {
            // Fully passable (normal walking)
            0x00 or 0x01 or 0x02 or 0x0A or 0x0B or 0x0E or 0x13 or 0x14 or 0x15 => "passable",

            // Water - requires surf
            0x04 or 0x05 or 0x07 or 0x08 or 0x81 => "water",

            // Waterfall - requires waterfall HM
            0x06 => "waterfall",

            // Ledges - one-way jump
            0x16 => "ledge_south",
            0x17 => "ledge_north",
            0x18 => "ledge_east",
            0x19 => "ledge_west",
            0x1A => "ledge_southeast",
            0x1B => "ledge_southwest",
            0x1C => "ledge_northeast",
            0x1D => "ledge_northwest",

            // Directional impassable
            0x38 => "impassable_south",
            0x39 => "impassable_north",
            0x3A => "impassable_east",
            0x3B => "impassable_west",

            // Ice/slides - passable but forced movement
            0x0F => "ice",
            0x10 => "ice_cracked",
            0x45 => "slide_south",
            0x46 => "slide_north",
            0x47 => "slide_east",
            0x48 => "slide_west",

            // Stairs - passable with forced direction
            0x1E => "stairs_south",
            0x1F => "stairs_north",

            // Forced walk directions
            0x41 => "walk_south",
            0x42 => "walk_north",
            0x43 => "walk_east",
            0x44 => "walk_west",

            // Counter - impassable but interactable
            0xC0 => "counter",

            // Bump/wall - fully impassable
            0x40 => "impassable",

            // Mountain - requires rock climb
            0x0C => "mountain",

            // Puddle - passable
            0x09 => "passable",

            // Hot spring/lava - special
            0x11 => "passable",
            0x12 => "hazard",

            // Warps - passable (trigger warp)
            0x80 or 0x82 or 0x83 => "warp",

            // Default - assume passable for unknown behaviors
            _ => "passable"
        };
    }

    #endregion

    #region Tileset IDs

    /// <summary>
    /// Transform tileset name to unified format.
    /// gTileset_General -> base:tileset:primary/general
    /// gTileset_Petalburg -> base:tileset:secondary/petalburg
    /// </summary>
    public static string TilesetId(string tilesetName, string tilesetType)
    {
        if (string.IsNullOrEmpty(tilesetName))
            return "";

        var name = tilesetName;
        if (name.StartsWith("gTileset_", StringComparison.OrdinalIgnoreCase))
            name = name[9..];

        return $"{Namespace}:tileset:{tilesetType}/{Normalize(name)}";
    }

    #endregion
}
