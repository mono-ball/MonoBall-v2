using System.Text.RegularExpressions;

namespace Porycon3.Services;

/// <summary>
/// Transforms pokeemerald IDs to PokeSharp unified format.
/// Format: {namespace}:{type}:{category}/{name} or {namespace}:{type}:{category}/{subcategory}/{name}
/// </summary>
public static class IdTransformer
{
    private const string Namespace = "base";
    private const string DefaultRegion = "hoenn";

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

    #region MapSec IDs

    /// <summary>
    /// Transform MAPSEC to unified format.
    /// MAPSEC_LITTLEROOT_TOWN -> base:mapsec:hoenn/littleroot_town
    /// </summary>
    public static string MapsecId(string pokeemeraldMapsec, string? region = null)
    {
        var name = pokeemeraldMapsec;
        if (name.StartsWith("MAPSEC_", StringComparison.OrdinalIgnoreCase))
            name = name[7..];

        return CreateId("mapsec", region ?? DefaultRegion, name);
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
    /// MUS_LITTLEROOT -> base:audio:music/towns/mus_littleroot
    /// </summary>
    public static string AudioId(string pokeemeraldMusic)
    {
        if (string.IsNullOrEmpty(pokeemeraldMusic))
            return "";

        var name = Normalize(pokeemeraldMusic);
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
    /// MAP_TYPE_ROUTE -> base:maptype:hoenn/route
    /// </summary>
    public static string MapTypeId(string pokeemeraldMapType, string? region = null)
    {
        if (string.IsNullOrEmpty(pokeemeraldMapType))
            return "";

        var name = pokeemeraldMapType;
        if (name.StartsWith("MAP_TYPE_", StringComparison.OrdinalIgnoreCase))
            name = name[9..];

        return CreateId("maptype", region ?? DefaultRegion, name);
    }

    #endregion

    #region Sprite IDs

    private static readonly string[] EliteFour = ["sidney", "phoebe", "glacia", "drake"];
    private static readonly string[] GymLeaders = ["brawly", "flannery", "juan", "liza", "norman",
        "roxanne", "tate", "wattson", "winona"];
    private static readonly string[] FrontierBrains = ["anabel", "brandon", "greta", "lucy",
        "noland", "spenser", "tucker"];

    // Variable sprite mappings: VAR_0 = rival, VAR_1 = player
    private static readonly Dictionary<string, string> VariableSpriteNames = new()
    {
        ["var_0"] = "var_rival",
        ["var_1"] = "var_player"
    };

    /// <summary>
    /// Transform graphics ID to sprite ID.
    /// OBJ_EVENT_GFX_BIRCH -> base:sprite:npcs/generic/birch
    /// OBJ_EVENT_GFX_MAY_NORMAL -> base:sprite:players/may/normal
    /// OBJ_EVENT_GFX_VAR_0 -> {base:sprite:npcs/generic/var_rival} (variable sprite)
    /// OBJ_EVENT_GFX_VAR_1 -> {base:sprite:npcs/generic/var_player} (variable sprite)
    /// </summary>
    public static string SpriteId(string graphicsId)
    {
        if (string.IsNullOrEmpty(graphicsId))
            return CreateId("sprite", "npcs", "unknown", "generic");

        var name = graphicsId;
        if (name.StartsWith("OBJ_EVENT_GFX_", StringComparison.OrdinalIgnoreCase))
            name = name[14..];

        name = Normalize(name);

        // Handle variable sprites (VAR_0 through VAR_F)
        // These are wrapped in curly braces to indicate runtime resolution
        if (name.StartsWith("var_"))
        {
            // Map known variable sprites to meaningful names
            var varName = VariableSpriteNames.TryGetValue(name, out var mapped) ? mapped : name;
            return $"{{{CreateId("sprite", "npcs", varName, "generic")}}}";
        }

        var subcategory = InferSpriteCategory(name);

        // Player characters: brendan_normal -> base:sprite:players/brendan/normal
        if (subcategory is "brendan" or "may")
        {
            var prefix = $"{subcategory}_";
            if (name.StartsWith(prefix))
            {
                var variant = name[prefix.Length..];
                return CreateId("sprite", "players", variant, subcategory);
            }
            return CreateId("sprite", "players", name, subcategory);
        }

        // NPCs: birch -> base:sprite:npcs/generic/birch
        return CreateId("sprite", "npcs", name, subcategory);
    }

    private static string InferSpriteCategory(string spriteName)
    {
        // Elite Four
        if (EliteFour.Any(n => spriteName == n || spriteName.StartsWith($"{n}_")))
            return "elite_four";

        // Gym Leaders
        if (GymLeaders.Any(n => spriteName == n || spriteName.StartsWith($"{n}_")))
            return "gym_leaders";

        // Frontier Brains
        if (FrontierBrains.Any(n => spriteName == n || spriteName.StartsWith($"{n}_")))
            return "frontier_brains";

        // Team Aqua
        if (spriteName is "archie" or "aqua_member_f" or "aqua_member_m" ||
            spriteName.StartsWith("aqua_") || spriteName.StartsWith("archie_"))
            return "team_aqua";

        // Team Magma
        if (spriteName is "maxie" or "magma_member_f" or "magma_member_m" ||
            spriteName.StartsWith("magma_") || spriteName.StartsWith("maxie_"))
            return "team_magma";

        // Player characters
        if (spriteName.StartsWith("brendan"))
            return "brendan";
        if (spriteName.StartsWith("may"))
            return "may";

        // Default: generic NPC
        return "generic";
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
