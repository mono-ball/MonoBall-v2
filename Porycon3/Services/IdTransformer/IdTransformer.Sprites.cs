namespace Porycon3.Services;

/// <summary>
/// Sprite-related ID transformations.
/// </summary>
public static partial class IdTransformer
{
    #region Sprite Data

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
    };

    // Pokemon overworld sprites - maps OBJ_EVENT_GFX names to species names for overworld sprites
    // These map to the extracted Pokemon overworld sprites: pokeemerald:pokemon:sprite/{species}/{species}overworld
    private static readonly Dictionary<string, string> PokemonSprites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["azumarill"] = "azumarill",
        ["azurill"] = "azurill",
        ["deoxys"] = "deoxys",
        ["deoxys_triangle"] = "deoxys",
        ["dusclops"] = "dusclops",
        ["groudon"] = "groudon",
        ["groudon_front"] = "groudon",
        ["groudon_asleep"] = "groudon",
        ["groudon_side"] = "groudon",
        ["ho_oh"] = "ho_oh",
        ["hooh"] = "ho_oh",
        ["kecleon"] = "kecleon",
        ["kecleon_bridge_shadow"] = "kecleon",
        ["kirlia"] = "kirlia",
        ["kyogre"] = "kyogre",
        ["kyogre_front"] = "kyogre",
        ["kyogre_asleep"] = "kyogre",
        ["kyogre_side"] = "kyogre",
        ["latias"] = "latias",
        ["latios"] = "latios",
        ["lugia"] = "lugia",
        ["mew"] = "mew",
        ["pikachu"] = "pikachu",
        ["poochyena"] = "poochyena",
        ["rayquaza"] = "rayquaza",
        ["rayquaza_cutscene"] = "rayquaza",
        ["rayquaza_still"] = "rayquaza",
        ["regice"] = "regice",
        ["regirock"] = "regirock",
        ["registeel"] = "registeel",
        ["skitty"] = "skitty",
        ["sudowoodo"] = "sudowoodo",
        ["wingull"] = "wingull",
        ["zigzagoon_1"] = "zigzagoon",
        ["zigzagoon_2"] = "zigzagoon",
        ["vigoroth"] = "vigoroth",
        ["vigoroth_carrying_box"] = "vigoroth",
        ["vigoroth_facing_away"] = "vigoroth"
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

    #endregion

    #region Sprite IDs

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
            return $"{Namespace}:sprite:characters/npcs/unknown";

        var name = graphicsId;
        if (name.StartsWith("OBJ_EVENT_GFX_", StringComparison.OrdinalIgnoreCase))
            name = name[14..];

        name = Normalize(name);

        // Check for sprite aliases first
        if (SpriteAliases.TryGetValue(name, out var aliasPath))
        {
            // Ensure aliases use characters/ prefix for NPCs
            if (aliasPath.StartsWith("npcs/"))
                return $"{Namespace}:sprite:characters/{aliasPath}";
            return $"{Namespace}:sprite:{aliasPath}";
        }

        // Check for Pokemon overworld sprites
        // Format: pokeemerald:pokemon:sprite/{species}/{species}_overworld
        if (PokemonSprites.TryGetValue(name, out var speciesName))
            return $"{Namespace}:pokemon:sprite/{speciesName}/{speciesName}_overworld";

        // Handle variable sprites (VAR_0 through VAR_F)
        if (name.StartsWith("var_"))
        {
            var varName = VariableSpriteNames.TryGetValue(name, out var mapped) ? mapped : name;
            return $"{Namespace}:sprite:characters/npcs/{{{varName}}}";
        }

        // Player characters: brendan_acro_bike -> base:sprite:characters/players/brendan/acrobike
        if (name.StartsWith("brendan_"))
        {
            var variant = name[8..].Replace("_", "");
            return $"{Namespace}:sprite:characters/players/brendan/{variant}";
        }
        if (name.StartsWith("may_"))
        {
            var variant = name[4..].Replace("_", "");
            return $"{Namespace}:sprite:characters/players/may/{variant}";
        }
        if (name.StartsWith("ruby_sapphire_brendan_"))
        {
            var variant = name[22..].Replace("_", "");
            return $"{Namespace}:sprite:characters/npcs/rubysapphirebrendan/{variant}";
        }
        if (name.StartsWith("ruby_sapphire_may_"))
        {
            var variant = name[18..].Replace("_", "");
            return $"{Namespace}:sprite:characters/npcs/rubysapphiremay/{variant}";
        }

        // Objects: dolls, cushions, misc items
        if (name.EndsWith("_doll") || name.StartsWith("big_") && name.EndsWith("_doll"))
        {
            var dollName = name.Replace("_", "");
            return $"{Namespace}:sprite:objects/dolls/{dollName}";
        }
        if (name.EndsWith("_cushion"))
        {
            var cushionName = name.Replace("_", "");
            return $"{Namespace}:sprite:objects/cushions/{cushionName}";
        }
        if (name is "item_ball" or "poke_ball")
            return $"{Namespace}:sprite:objects/misc/pokeball";
        if (name == "birchs_bag")
            return $"{Namespace}:sprite:objects/misc/birchsbag";

        // Berry tree sprites
        if (name == "berry_tree")
            return $"{Namespace}:sprite:objects/berrytrees/cheri";
        if (name.StartsWith("berry_tree_"))
        {
            // Extract tree name after "berry_tree_" prefix (e.g., "berry_tree_cheri" -> "cheri")
            var treeName = name.Substring(11).Replace("_", "");
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
            return $"{Namespace}:sprite:characters/npcs/{category}/{npcName}";

        // Generic NPCs
        var npcNameNoUnderscores = name.Replace("_", "");
        return $"{Namespace}:sprite:characters/npcs/{npcNameNoUnderscores}";
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
}
