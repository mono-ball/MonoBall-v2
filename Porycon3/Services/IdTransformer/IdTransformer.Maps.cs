namespace Porycon3.Services;

/// <summary>
/// Map-related ID transformations.
/// </summary>
public static partial class IdTransformer
{
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

        return CreateId("map", region ?? Region, name);
    }

    /// <summary>
    /// Transform map name to unified format.
    /// LittlerootTown -> base:map:hoenn/littleroot_town
    /// </summary>
    public static string MapIdFromName(string mapName, string? region = null)
    {
        return CreateId("map", region ?? Region, mapName);
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

        return CreateId("section", region ?? Region, name);
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

        return CreateId("weather", region ?? Region, name);
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

        return CreateId("battlescene", region ?? Region, name);
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
}
