using System.Text.RegularExpressions;

namespace Porycon3.Services;

/// <summary>
/// Transforms pokeemerald IDs to PokeSharp unified format.
/// Format: {namespace}:{type}:{category}/{name} or {namespace}:{type}:{category}/{subcategory}/{name}
/// </summary>
public static class IdTransformer
{
    private static string _namespace = "base";
    private const string DefaultRegion = "hoenn";

    /// <summary>
    /// The namespace prefix for all generated IDs (e.g., "base", "emerald-audio").
    /// Default is "base".
    /// </summary>
    public static string Namespace
    {
        get => _namespace;
        set => _namespace = value ?? "base";
    }

    #region Normalization

    /// <summary>
    /// Normalize a string to lowercase with underscores.
    /// Converts CamelCase to snake_case, handles floor suffixes.
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        // Convert CamelCase to snake_case
        var s1 = Regex.Replace(value, "(.)([A-Z][a-z]+)", "$1_$2");
        var s2 = Regex.Replace(s1, "([a-z0-9])([A-Z])", "$1_$2");

        // Replace spaces and hyphens with underscores
        var s3 = Regex.Replace(s2, @"[\s\-]+", "_");

        // Remove non-alphanumeric except underscore
        var s4 = Regex.Replace(s3.ToLowerInvariant(), @"[^a-z0-9_]", "");

        // Collapse multiple underscores
        var s5 = Regex.Replace(s4, @"_+", "_");

        // Remove leading/trailing underscores
        var s6 = s5.Trim('_');

        // Fix floor suffixes: _1_f -> _1f, _b1_f -> _b1f
        var s7 = Regex.Replace(s6, @"_(\d+)_([fr])($|_)", "_$1$2$3");
        var s8 = Regex.Replace(s7, @"_b(\d+)_([fr])($|_)", "_b$1$2$3");

        return s8;
    }

    private static string CreateId(string entityType, string category, string name, string? subcategory = null)
    {
        entityType = Normalize(entityType);
        category = Normalize(category);
        name = Normalize(name);

        if (!string.IsNullOrEmpty(subcategory))
        {
            subcategory = Normalize(subcategory);
            return $"{Namespace}:{entityType}:{category}/{subcategory}/{name}";
        }

        return $"{Namespace}:{entityType}:{category}/{name}";
    }

    #endregion

    #region Map IDs

    /// <summary>
    /// Transform pokeemerald map ID to unified format.
    /// MAP_LITTLEROOT_TOWN -> base:map:hoenn/littleroot_town
    /// </summary>
    public static string MapId(string pokeemeraldMapId, string? region = null)
    {
        var name = pokeemeraldMapId;
        if (name.StartsWith("MAP_", StringComparison.OrdinalIgnoreCase))
            name = name[4..];

        return CreateId("map", region ?? DefaultRegion, name);
    }

    /// <summary>
    /// Transform map name to unified format.
    /// LittlerootTown -> base:map:hoenn/littleroot_town
    /// </summary>
    public static string MapIdFromName(string mapName, string? region = null)
    {
        return CreateId("map", region ?? DefaultRegion, mapName);
    }

    #endregion

    #region MapSection IDs

    /// <summary>
    /// Transform MAPSEC to unified format.
    /// MAPSEC_LITTLEROOT_TOWN -> base:section:hoenn/littleroot_town
    /// </summary>
    public static string MapsecId(string pokeemeraldMapsec, string? region = null)
    {
        var name = pokeemeraldMapsec;
        if (name.StartsWith("MAPSEC_", StringComparison.OrdinalIgnoreCase))
            name = name[7..];

        return CreateId("section", region ?? DefaultRegion, name);
    }

    #endregion

    #region Weather IDs

    /// <summary>
    /// Transform weather to unified format.
    /// WEATHER_SUNNY -> base:weather:hoenn/sunny
    /// </summary>
    public static string WeatherId(string pokeemeraldWeather, string? region = null)
    {
        if (string.IsNullOrEmpty(pokeemeraldWeather))
            return "";

        var name = pokeemeraldWeather;
        if (name.StartsWith("WEATHER_", StringComparison.OrdinalIgnoreCase))
            name = name[8..];

        return CreateId("weather", region ?? DefaultRegion, name);
    }

    #endregion

    #region Audio IDs

    private static readonly Dictionary<string, string[]> MusicCategories = new()
    {
        ["towns"] = ["town", "city", "village", "littleroot", "oldale", "petalburg",
            "rustboro", "dewford", "slateport", "mauville", "verdanturf",
            "fallarbor", "lavaridge", "fortree", "lilycove", "mossdeep",
            "sootopolis", "pacifidlog", "ever_grande"],
        ["routes"] = ["route", "cycling", "surf", "sailing", "diving", "underwater"],
        ["battle"] = ["battle", "vs_", "encounter", "trainer_battle", "wild_battle",
            "gym_leader", "elite", "champion", "frontier", "victory"],
        ["fanfares"] = ["fanfare", "jingle", "level_up", "evolution", "heal",
            "obtained", "pokemon_get", "badge_get", "intro"],
        ["special"] = ["cave", "forest", "desert", "abandoned", "team_aqua",
            "team_magma", "legendary", "credits", "title", "ending"]
    };

    /// <summary>
    /// Transform music constant to unified format.
    /// MUS_LITTLEROOT -> base:audio:music/towns/littleroot
    /// SE_DOOR -> base:audio:music/sfx/door
    /// </summary>
    public static string AudioId(string pokeemeraldMusic)
    {
        if (string.IsNullOrEmpty(pokeemeraldMusic))
            return "";

        var name = Normalize(pokeemeraldMusic);

        // Strip audio prefixes to match SoundExtractor output
        if (name.StartsWith("mus_"))
            name = name[4..];
        else if (name.StartsWith("se_"))
            name = name[3..];
        else if (name.StartsWith("ph_"))
            name = name[3..];

        var subcategory = CategorizeMusic(name);

        return CreateId("audio", "music", name, subcategory);
    }

    private static string CategorizeMusic(string name)
    {
        foreach (var (category, keywords) in MusicCategories)
        {
            if (keywords.Any(k => name.Contains(k)))
                return category;
        }
        return "special";
    }

    #endregion

    #region Battle Scene IDs

    /// <summary>
    /// Transform battle scene to unified format.
    /// MAP_BATTLE_SCENE_NORMAL -> base:battlescene:hoenn/normal
    /// </summary>
    public static string BattleSceneId(string pokeemeraldBattleScene, string? region = null)
    {
        if (string.IsNullOrEmpty(pokeemeraldBattleScene))
            return "";

        var name = pokeemeraldBattleScene;
        if (name.StartsWith("MAP_BATTLE_SCENE_", StringComparison.OrdinalIgnoreCase))
            name = name[17..];

        return CreateId("battlescene", region ?? DefaultRegion, name);
    }

    #endregion

    #region Map Type IDs

    /// <summary>
    /// Transform map type to unified format.
    /// MAP_TYPE_ROUTE -> base:maptype:route
    /// </summary>
    public static string MapTypeId(string pokeemeraldMapType)
    {
        if (string.IsNullOrEmpty(pokeemeraldMapType))
            return "";

        var name = pokeemeraldMapType;
        if (name.StartsWith("MAP_TYPE_", StringComparison.OrdinalIgnoreCase))
            name = name[9..];

        return $"{Namespace}:maptype:{Normalize(name)}";
    }

    #endregion

    #region Sprite IDs

    private static readonly string[] EliteFour = ["sidney", "phoebe", "glacia", "drake"];
    private static readonly string[] GymLeaders = ["brawly", "flannery", "juan", "liza", "norman",
        "roxanne", "tate", "wattson", "winona"];
    private static readonly string[] FrontierBrains = ["anabel", "brandon", "greta", "lucy",
        "noland", "spenser", "tucker"];

    // Misc objects that go in objects/misc/ instead of npcs/
    private static readonly HashSet<string> MiscObjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "apricorn_tree", "birth_island_stone", "breakable_rock", "cable_car",
        "cuttable_tree", "fossil", "light", "mart_light", "moving_box",
        "mr_brineys_boat", "poke_center_light", "pushable_boulder",
        "ss_tidal", "statue", "submarine_shadow", "truck"
        // Note: vigoroth sprites are in pokemon/ folder, not objects/misc
    };

    // Pokemon overworld sprites - maps OBJ_EVENT_GFX names to extracted sprite IDs
    private static readonly Dictionary<string, string> PokemonSprites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["azumarill"] = "pokemon/azumarillold",
        ["azurill"] = "pokemon/azurillold",
        ["deoxys"] = "pokemon/deoxysold",
        ["deoxys_triangle"] = "pokemon/deoxysold", // same sprite
        ["dusclops"] = "pokemon/dusclopsold",
        ["groudon"] = "pokemon/groudonfront",
        ["groudon_front"] = "pokemon/groudonfront",
        ["groudon_asleep"] = "pokemon/groudonside", // asleep uses side view sprite
        ["groudon_side"] = "pokemon/groudonside",
        ["ho_oh"] = "pokemon/hoohold",
        ["hooh"] = "pokemon/hoohold",
        ["kecleon"] = "pokemon/kecleonold",
        ["kecleon_bridge_shadow"] = "pokemon/kecleonold", // same sprite
        ["kirlia"] = "pokemon/kirliaold",
        ["kyogre"] = "pokemon/kyogrefront",
        ["kyogre_front"] = "pokemon/kyogrefront",
        ["kyogre_asleep"] = "pokemon/kyogreside", // asleep uses side view sprite
        ["kyogre_side"] = "pokemon/kyogreside",
        ["lugia"] = "pokemon/lugiaold",
        ["mew"] = "pokemon/mewold",
        ["pikachu"] = "pokemon/pikachuold",
        ["poochyena"] = "pokemon/poochyenaold",
        ["rayquaza"] = "pokemon/rayquazacutscene",
        ["rayquaza_cutscene"] = "pokemon/rayquazacutscene",
        ["rayquaza_still"] = "pokemon/rayquazastill",
        ["regice"] = "pokemon/regi", // all regis use same sprite
        ["regirock"] = "pokemon/regi",
        ["registeel"] = "pokemon/regi",
        ["skitty"] = "pokemon/skittyold",
        ["sudowoodo"] = "pokemon/sudowoodotree",
        ["wingull"] = "pokemon/wingullold",
        ["zigzagoon_1"] = "pokemon/zigzagoonold",
        ["zigzagoon_2"] = "pokemon/enemyzigzagoon",
        ["vigoroth_carrying_box"] = "pokemon/vigorothcarryingbox",
        ["vigoroth_facing_away"] = "pokemon/vigorothfacingaway"
    };

    // Sprite aliases: some OBJ_EVENT_GFX names map to different sprite names
    private static readonly Dictionary<string, string> SpriteAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trick_house_statue"] = "objects/misc/statue",
        ["rival_brendan_normal"] = "npcs/rubysapphirebrendan/rubysapphirebrendan",
        ["rival_may_normal"] = "npcs/rubysapphiremay/rubysapphiremay",
        ["mystery_gift_man"] = "npcs/mysteryeventdeliveryman",
        ["union_room_nurse"] = "npcs/nurse"
    };

    // Variable sprite mappings: VAR_0 = rival, VAR_1 = player
    private static readonly Dictionary<string, string> VariableSpriteNames = new()
    {
        ["var_0"] = "var_rival",
        ["var_1"] = "var_player"
    };

    /// <summary>
    /// Transform graphics ID to sprite ID.
    /// OBJ_EVENT_GFX_BIRCH -> base:sprite:npcs/birch
    /// OBJ_EVENT_GFX_MAY_NORMAL -> base:sprite:players/may/normal
    /// OBJ_EVENT_GFX_PIKACHU_DOLL -> base:sprite:objects/dolls/pikachudoll
    /// OBJ_EVENT_GFX_ITEM_BALL -> base:sprite:objects/misc/itemball
    /// OBJ_EVENT_GFX_VAR_0 -> base:sprite:npcs/{var_rival} (variable sprite)
    /// </summary>
    public static string SpriteId(string graphicsId)
    {
        if (string.IsNullOrEmpty(graphicsId))
            return $"{Namespace}:sprite:npcs/unknown";

        var name = graphicsId;
        if (name.StartsWith("OBJ_EVENT_GFX_", StringComparison.OrdinalIgnoreCase))
            name = name[14..];

        name = Normalize(name);

        // Check for sprite aliases first
        if (SpriteAliases.TryGetValue(name, out var aliasPath))
        {
            return $"{Namespace}:sprite:{aliasPath}";
        }

        // Check for Pokemon overworld sprites
        if (PokemonSprites.TryGetValue(name, out var pokemonPath))
        {
            return $"{Namespace}:sprite:{pokemonPath}";
        }

        // Handle variable sprites (VAR_0 through VAR_F)
        // Only the variable name is wrapped in curly braces to indicate runtime resolution
        if (name.StartsWith("var_"))
        {
            var varName = VariableSpriteNames.TryGetValue(name, out var mapped) ? mapped : name;
            return $"{Namespace}:sprite:npcs/{{{varName}}}";
        }

        // Player characters: brendan_acro_bike -> base:sprite:players/brendan/acrobike
        if (name.StartsWith("brendan_"))
        {
            var variant = name[8..].Replace("_", ""); // Remove "brendan_" and underscores
            return $"{Namespace}:sprite:players/brendan/{variant}";
        }
        if (name.StartsWith("may_"))
        {
            var variant = name[4..].Replace("_", ""); // Remove "may_" and underscores
            return $"{Namespace}:sprite:players/may/{variant}";
        }
        if (name.StartsWith("ruby_sapphire_brendan_"))
        {
            var variant = name[22..].Replace("_", "");
            return $"{Namespace}:sprite:npcs/rubysapphirebrendan/{variant}";
        }
        if (name.StartsWith("ruby_sapphire_may_"))
        {
            var variant = name[18..].Replace("_", "");
            return $"{Namespace}:sprite:npcs/rubysapphiremay/{variant}";
        }

        // Objects: dolls, cushions, misc items
        if (name.EndsWith("_doll") || name.StartsWith("big_") && name.EndsWith("_doll"))
        {
            // Remove underscores to match SpriteExtractor PascalCase->lowercase: pikachu_doll -> pikachudoll
            var dollName = name.Replace("_", "");
            return $"{Namespace}:sprite:objects/dolls/{dollName}";
        }
        if (name.EndsWith("_cushion"))
        {
            var cushionName = name.Replace("_", "");
            return $"{Namespace}:sprite:objects/cushions/{cushionName}";
        }
        // item_ball and poke_ball both use the same PokeBall sprite
        if (name is "item_ball" or "poke_ball")
        {
            return $"{Namespace}:sprite:objects/misc/pokeball";
        }
        if (name == "birchs_bag")
        {
            return $"{Namespace}:sprite:objects/misc/birchsbag";
        }
        // Berry tree sprites - generic berry_tree uses cheri as default, specific berries use their name
        if (name == "berry_tree")
        {
            // Generic berry tree uses Cheri as the default visual
            return $"{Namespace}:sprite:objects/berrytrees/cheri";
        }
        if (name.StartsWith("berry_tree_"))
        {
            // berry_tree_early_stages -> berrytreeearlystages (these are separate sprites)
            var treeName = name.Replace("_", "");
            return $"{Namespace}:sprite:objects/berrytrees/{treeName}";
        }

        // Misc objects (truck, fossil, pushable_boulder, etc.)
        if (MiscObjects.Contains(name))
        {
            var objName = name.Replace("_", "");
            return $"{Namespace}:sprite:objects/misc/{objName}";
        }

        // NPCs with special categories
        var (category, npcName) = InferNpcCategoryAndName(name);
        if (!string.IsNullOrEmpty(category))
        {
            return $"{Namespace}:sprite:npcs/{category}/{npcName}";
        }

        // Generic NPCs: black_belt -> base:sprite:npcs/blackbelt (no underscores to match PascalCase->lowercase)
        var npcNameNoUnderscores = name.Replace("_", "");
        return $"{Namespace}:sprite:npcs/{npcNameNoUnderscores}";
    }

    private static (string Category, string Name) InferNpcCategoryAndName(string spriteName)
    {
        // Elite Four
        foreach (var n in EliteFour)
        {
            if (spriteName == n || spriteName.StartsWith($"{n}_"))
                return ("elitefour", spriteName.Replace("_", ""));
        }

        // Gym Leaders
        foreach (var n in GymLeaders)
        {
            if (spriteName == n || spriteName.StartsWith($"{n}_"))
                return ("gymleaders", spriteName.Replace("_", ""));
        }

        // Frontier Brains
        foreach (var n in FrontierBrains)
        {
            if (spriteName == n || spriteName.StartsWith($"{n}_"))
                return ("frontierbrains", spriteName.Replace("_", ""));
        }

        // Team Aqua
        if (spriteName is "archie" or "aqua_member_f" or "aqua_member_m" ||
            spriteName.StartsWith("aqua_") || spriteName.StartsWith("archie_"))
            return ("teamaqua", spriteName.Replace("_", ""));

        // Team Magma
        if (spriteName is "maxie" or "magma_member_f" or "magma_member_m" ||
            spriteName.StartsWith("magma_") || spriteName.StartsWith("maxie_"))
            return ("teammagma", spriteName.Replace("_", ""));

        // Default: no category (goes directly under npcs/)
        return ("", spriteName);
    }

    #endregion

    #region Behavior IDs

    /// <summary>
    /// Transform movement type to behavior ID.
    /// MOVEMENT_TYPE_LOOK_AROUND -> base:script:behavior/look_around
    /// </summary>
    public static string BehaviorId(string movementType)
    {
        if (string.IsNullOrEmpty(movementType))
            return "";

        var name = movementType;
        if (name.StartsWith("MOVEMENT_TYPE_", StringComparison.OrdinalIgnoreCase))
            name = name[14..];

        return CreateId("script", "behavior", name);
    }

    #endregion

    #region Metatile Behavior IDs

    /// <summary>
    /// Transform metatile behavior value to behavior ID.
    /// 0 (MB_NORMAL) -> null (default, not stored)
    /// Other values -> base:behavior:tiles/{name}
    /// Behavior value is extracted from metatile_attributes.bin (bits 0-7).
    /// </summary>
    public static string? MetatileBehaviorId(int behaviorValue)
    {
        // Extract pure behavior from bits 0-7 only
        var pureBehavior = behaviorValue & 0xFF;

        // Normal behavior is the default - return null to omit from output
        if (pureBehavior == 0)
            return null;

        var name = GetMetatileBehaviorName(pureBehavior);
        return $"{Namespace}:behavior:tiles/{name}";
    }

    /// <summary>
    /// Get behavior name from behavior value.
    /// Maps raw behavior values to human-readable names.
    /// </summary>
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
    /// Terrain value is derived from behavior.
    /// </summary>
    public static string? TerrainTypeId(int terrainValue)
    {
        // Normal terrain is the default - return null to omit from output
        if (terrainValue == 0)
            return null;

        var name = GetTerrainTypeName(terrainValue);
        return $"{Namespace}:terrain:{name}";
    }

    /// <summary>
    /// Get terrain name from terrain type value.
    /// </summary>
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

    #region Collision

    /// <summary>
    /// Derive collision ID from behavior value.
    /// Returns null for "passable" (default), otherwise "base:collision:{type}".
    /// </summary>
    public static string? DeriveCollisionId(int behavior)
    {
        var collisionType = GetCollisionType(behavior);

        // Passable is the default - return null to omit from output
        if (collisionType == "passable")
            return null;

        return $"{Namespace}:collision:{collisionType}";
    }

    /// <summary>
    /// Get collision type name from behavior value.
    /// </summary>
    private static string GetCollisionType(int behavior)
    {
        // Extract pure behavior from bits 0-7
        var pureBehavior = behavior & 0xFF;

        return pureBehavior switch
        {
            // Fully passable (normal walking)
            0x00 => "passable",           // MB_NORMAL
            0x01 => "passable",           // MB_TALL_GRASS
            0x02 => "passable",           // MB_VERY_TALL_GRASS
            0x0A => "passable",           // MB_NO_RUNNING
            0x0B => "passable",           // MB_INDOOR_ENCOUNTER
            0x0E => "passable",           // MB_FOOTPRINTS
            0x13 => "passable",           // MB_SAND
            0x14 => "passable",           // MB_ASH_GRASS
            0x15 => "passable",           // MB_SAND_CAVE

            // Water - requires surf
            0x04 => "water",              // MB_SHORE_WATER
            0x05 => "water",              // MB_DEEP_WATER
            0x07 => "water",              // MB_OCEAN_WATER
            0x08 => "water",              // MB_POND_WATER
            0x81 => "water",              // MB_DEEP_WATER_2

            // Waterfall - requires waterfall HM
            0x06 => "waterfall",          // MB_WATERFALL

            // Ledges - one-way jump
            0x16 => "ledge_south",        // MB_LEDGE_SOUTH
            0x17 => "ledge_north",        // MB_LEDGE_NORTH
            0x18 => "ledge_east",         // MB_LEDGE_EAST
            0x19 => "ledge_west",         // MB_LEDGE_WEST
            0x1A => "ledge_southeast",    // MB_LEDGE_SOUTHEAST
            0x1B => "ledge_southwest",    // MB_LEDGE_SOUTHWEST
            0x1C => "ledge_northeast",    // MB_LEDGE_NORTHEAST
            0x1D => "ledge_northwest",    // MB_LEDGE_NORTHWEST

            // Directional impassable
            0x38 => "impassable_south",   // MB_IMPASSABLE_SOUTH
            0x39 => "impassable_north",   // MB_IMPASSABLE_NORTH
            0x3A => "impassable_east",    // MB_IMPASSABLE_EAST
            0x3B => "impassable_west",    // MB_IMPASSABLE_WEST

            // Ice/slides - passable but forced movement
            0x0F => "ice",                // MB_THIN_ICE
            0x10 => "ice_cracked",        // MB_CRACKED_ICE
            0x45 => "slide_south",        // MB_SLIDE_SOUTH
            0x46 => "slide_north",        // MB_SLIDE_NORTH
            0x47 => "slide_east",         // MB_SLIDE_EAST
            0x48 => "slide_west",         // MB_SLIDE_WEST

            // Stairs - passable with forced direction
            0x1E => "stairs_south",       // MB_STAIRS_SOUTH
            0x1F => "stairs_north",       // MB_STAIRS_NORTH

            // Forced walk directions
            0x41 => "walk_south",         // MB_WALK_SOUTH
            0x42 => "walk_north",         // MB_WALK_NORTH
            0x43 => "walk_east",          // MB_WALK_EAST
            0x44 => "walk_west",          // MB_WALK_WEST

            // Counter - impassable but interactable
            0xC0 => "counter",            // MB_COUNTER

            // Bump/wall - fully impassable
            0x40 => "impassable",         // MB_BUMP

            // Mountain - requires rock climb
            0x0C => "mountain",           // MB_MOUNTAIN

            // Puddle - passable
            0x09 => "passable",           // MB_PUDDLE

            // Hot spring/lava - special
            0x11 => "passable",           // MB_HOT_SPRING
            0x12 => "hazard",             // MB_LAVA

            // Warps - passable (trigger warp)
            0x80 => "warp",               // MB_WARP_OR_BRIDGE
            0x82 => "warp",               // MB_WARP_DOOR
            0x83 => "warp",               // MB_WARP_OR_BRIDGE_2

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

    #region Flag IDs

    private static readonly Dictionary<string, string> FlagPrefixes = new()
    {
        ["FLAG_HIDE_"] = "visibility",
        ["FLAG_HIDDEN_ITEM_"] = "hidden_item",
        ["FLAG_ITEM_"] = "item",
        ["FLAG_TEMP_"] = "temporary",
        ["FLAG_DECORATION_"] = "decoration",
        ["FLAG_DEFEATED_"] = "defeated",
        ["FLAG_TRAINER_"] = "trainer",
        ["FLAG_BADGE_"] = "badge",
        ["FLAG_RECEIVED_"] = "received",
        ["FLAG_DAILY_"] = "daily",
        ["FLAG_ENCOUNTERED_"] = "encountered",
        ["FLAG_UNLOCKED_"] = "unlock",
        ["FLAG_COMPLETED_"] = "story",
        ["FLAG_TRIGGERED_"] = "trigger",
        ["FLAG_INTERACTED_"] = "interaction",
        ["FLAG_CAUGHT_"] = "collection"
    };

    /// <summary>
    /// Transform flag to unified format.
    /// FLAG_HIDE_LITTLEROOT_TOWN_FAT_MAN -> base:flag:visibility/littleroot_town_fat_man
    /// </summary>
    public static string FlagId(string pokeemeraldFlag)
    {
        if (string.IsNullOrEmpty(pokeemeraldFlag) || pokeemeraldFlag == "0")
            return "";

        var flagName = pokeemeraldFlag;
        var category = "misc";

        // Determine category from prefix
        foreach (var (prefix, cat) in FlagPrefixes)
        {
            if (flagName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                flagName = flagName[prefix.Length..];
                category = cat;
                break;
            }
        }

        // Handle generic FLAG_ prefix
        if (category == "misc" && flagName.StartsWith("FLAG_", StringComparison.OrdinalIgnoreCase))
        {
            flagName = flagName[5..];
        }

        return CreateId("flag", category, flagName);
    }

    #endregion

    #region Script IDs

    /// <summary>
    /// Transform script reference to script ID.
    /// LittlerootTown_EventScript_Twin -> base:script:map/littleroot_town_event_script_twin
    /// </summary>
    public static string ScriptId(string pokeemeraldScript)
    {
        if (string.IsNullOrEmpty(pokeemeraldScript) ||
            pokeemeraldScript == "NULL" ||
            pokeemeraldScript == "0x0" ||
            pokeemeraldScript == "0")
            return "";

        return CreateId("script", "map", pokeemeraldScript);
    }

    /// <summary>
    /// Transform script reference to interaction ID.
    /// LittlerootTown_EventScript_Twin -> base:script:interaction/littleroot_town_event_script_twin
    /// </summary>
    public static string InteractionId(string pokeemeraldScript)
    {
        if (string.IsNullOrEmpty(pokeemeraldScript) ||
            pokeemeraldScript == "NULL" ||
            pokeemeraldScript == "0x0" ||
            pokeemeraldScript == "0")
            return "";

        return CreateId("script", "interaction", pokeemeraldScript);
    }

    #endregion

    #region Trainer IDs

    /// <summary>
    /// Transform trainer type to trainer ID.
    /// TRAINER_TYPE_NORMAL -> base:trainer:normal/default
    /// </summary>
    public static string TrainerId(string trainerType)
    {
        if (string.IsNullOrEmpty(trainerType) || trainerType == "TRAINER_TYPE_NONE")
            return "";

        var name = trainerType;
        if (name.StartsWith("TRAINER_TYPE_", StringComparison.OrdinalIgnoreCase))
            name = name[13..];
        else if (name.StartsWith("TRAINER_", StringComparison.OrdinalIgnoreCase))
            name = name[8..];

        var normalized = Normalize(name);
        var parts = normalized.Split('_', 2);

        if (parts.Length == 2)
            return CreateId("trainer", parts[0], parts[1]);

        return CreateId("trainer", parts[0], "default");
    }

    #endregion

    #region Variable IDs

    /// <summary>
    /// Transform variable to unified format.
    /// VAR_ROUTE101_STATE -> base:variable:hoenn/route101_state
    /// </summary>
    public static string VariableId(string pokeemeraldVar, string? region = null)
    {
        if (string.IsNullOrEmpty(pokeemeraldVar))
            return "";

        var name = pokeemeraldVar;
        if (name.StartsWith("VAR_", StringComparison.OrdinalIgnoreCase))
            name = name[4..];

        return CreateId("variable", region ?? DefaultRegion, name);
    }

    #endregion
}
