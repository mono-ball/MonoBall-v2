namespace Porycon3.Services;

/// <summary>
/// Audio-related ID transformations.
/// </summary>
public static partial class IdTransformer
{
    #region Audio Categorization

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

    #endregion

    #region Audio IDs

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
}
