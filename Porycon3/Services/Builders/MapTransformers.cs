namespace Porycon3.Services.Builders;

/// <summary>
/// Transforms weather and battle scene IDs for map output.
/// </summary>
public static class MapTransformers
{
    /// <summary>
    /// Transform weather constant to weather ID.
    /// </summary>
    public static string TransformWeatherId(string weather)
    {
        if (string.IsNullOrEmpty(weather))
            return $"{IdTransformer.Namespace}:weather:outdoor/sunny";

        var name = weather.StartsWith("WEATHER_", StringComparison.OrdinalIgnoreCase)
            ? weather[8..].ToLowerInvariant()
            : weather.ToLowerInvariant();

        return $"{IdTransformer.Namespace}:weather:outdoor/{name}";
    }

    /// <summary>
    /// Transform battle scene constant to battle scene ID.
    /// </summary>
    public static string TransformBattleSceneId(string battleScene)
    {
        if (string.IsNullOrEmpty(battleScene))
            return $"{IdTransformer.Namespace}:battlescene:normal/normal";

        var name = battleScene.StartsWith("MAP_BATTLE_SCENE_", StringComparison.OrdinalIgnoreCase)
            ? battleScene[17..].ToLowerInvariant()
            : battleScene.ToLowerInvariant();

        return $"{IdTransformer.Namespace}:battlescene:normal/{name}";
    }
}
