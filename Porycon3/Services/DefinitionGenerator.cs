using System.Text.Json;

namespace Porycon3.Services;

/// <summary>
/// Generates additional definition files (Weather, BattleScenes, Regions)
/// based on IDs referenced by converted maps.
/// Matches porycon2's definition_converter.py output format.
/// </summary>
public class DefinitionGenerator
{
    private readonly string _outputPath;
    private readonly string _region;
    private readonly object _lock = new();
    private readonly HashSet<string> _weatherIds = new();
    private readonly HashSet<string> _battleSceneIds = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Weather configurations matching porycon2
    private static readonly Dictionary<string, WeatherConfig> WeatherConfigs = new()
    {
        ["sunny"] = new(1.0, true, "#FFD700", 0.1),
        ["sunny_clouds"] = new(1.0, false, EffectScriptId: "base:script:weather/clouds"),
        ["rain"] = new(1.0, true, EffectScriptId: "base:script:weather/rain", AmbientSoundId: "base:audio:sfx/ambient/rain"),
        ["rain_thunderstorm"] = new(1.5, true, EffectScriptId: "base:script:weather/thunderstorm", AmbientSoundId: "base:audio:sfx/ambient/thunder", ReducesVisibility: true, VisibilityRange: 6),
        ["downpour"] = new(2.0, true, EffectScriptId: "base:script:weather/downpour", AmbientSoundId: "base:audio:sfx/ambient/heavy_rain", ReducesVisibility: true, VisibilityRange: 4),
        ["snow"] = new(1.0, true, EffectScriptId: "base:script:weather/snow"),
        ["sandstorm"] = new(1.0, true, EffectScriptId: "base:script:weather/sandstorm", ReducesVisibility: true, VisibilityRange: 5),
        ["fog_horizontal"] = new(0.8, false, EffectScriptId: "base:script:weather/fog_horizontal", ReducesVisibility: true, VisibilityRange: 4),
        ["fog_diagonal"] = new(0.8, false, EffectScriptId: "base:script:weather/fog_diagonal", ReducesVisibility: true, VisibilityRange: 5),
        ["volcanic_ash"] = new(1.0, false, "#808080", 0.3, EffectScriptId: "base:script:weather/ash"),
        ["underwater_bubbles"] = new(0.5, false, "#0066CC", 0.2, EffectScriptId: "base:script:weather/bubbles"),
        ["shade"] = new(0.7, false, "#404040", 0.2),
        ["drought"] = new(2.0, true, "#FF6600", 0.15, EffectScriptId: "base:script:weather/drought"),
        ["none"] = new(0.0, false)
    };

    // Battle scene configurations matching porycon2
    private static readonly Dictionary<string, BattleSceneConfig> BattleSceneConfigs = new()
    {
        ["normal"] = new("normal", "tall_grass"),
        ["grass"] = new("normal", "tall_grass"),
        ["long_grass"] = new("normal", "long_grass"),
        ["sand"] = new("normal", "sand"),
        ["water"] = new("normal", "water", HasAnimatedBackground: true),
        ["pond"] = new("normal", "pond_water", HasAnimatedBackground: true),
        ["cave"] = new("normal", "cave"),
        ["rock"] = new("normal", "rock"),
        ["building"] = new("indoor", "building"),
        ["gym"] = new("gym", "building", PaletteId: "base:texture:battle/palette/gym"),
        ["frontier"] = new("frontier", "building", PaletteId: "base:texture:battle/palette/frontier"),
        ["aqua"] = new("team", "stadium", PaletteId: "base:texture:battle/palette/aqua"),
        ["magma"] = new("team", "stadium", PaletteId: "base:texture:battle/palette/magma"),
        ["sidney"] = new("elite_four", "stadium", PaletteId: "base:texture:battle/palette/elite_sidney", DefaultMusicId: "base:audio:music/battle/elite_four"),
        ["phoebe"] = new("elite_four", "stadium", PaletteId: "base:texture:battle/palette/elite_phoebe", DefaultMusicId: "base:audio:music/battle/elite_four"),
        ["glacia"] = new("elite_four", "stadium", PaletteId: "base:texture:battle/palette/elite_glacia", DefaultMusicId: "base:audio:music/battle/elite_four"),
        ["drake"] = new("elite_four", "stadium", PaletteId: "base:texture:battle/palette/elite_drake", DefaultMusicId: "base:audio:music/battle/elite_four"),
        ["champion"] = new("champion", "stadium", PaletteId: "base:texture:battle/palette/champion", DefaultMusicId: "base:audio:music/battle/champion")
    };

    public DefinitionGenerator(string inputPath, string outputPath, string region)
    {
        _outputPath = outputPath;
        _region = region;
    }

    /// <summary>
    /// Track a weather ID for later generation (thread-safe).
    /// </summary>
    public void TrackWeatherId(string? weatherId)
    {
        if (!string.IsNullOrEmpty(weatherId))
        {
            lock (_lock)
            {
                _weatherIds.Add(weatherId);
            }
        }
    }

    /// <summary>
    /// Track a battle scene ID for later generation (thread-safe).
    /// </summary>
    public void TrackBattleSceneId(string? battleSceneId)
    {
        if (!string.IsNullOrEmpty(battleSceneId))
        {
            lock (_lock)
            {
                _battleSceneIds.Add(battleSceneId);
            }
        }
    }

    /// <summary>
    /// Generate all tracked definitions.
    /// </summary>
    public (int Weather, int BattleScenes, bool Region) GenerateAll()
    {
        var weatherCount = GenerateWeatherDefinitions();
        var battleSceneCount = GenerateBattleSceneDefinitions();
        var regionGenerated = GenerateRegionDefinition();
        return (weatherCount, battleSceneCount, regionGenerated);
    }

    private int GenerateWeatherDefinitions()
    {
        var weatherDir = Path.Combine(_outputPath, "Definitions", "Weather");
        Directory.CreateDirectory(weatherDir);

        int count = 0;
        foreach (var weatherId in _weatherIds)
        {
            // base:weather:outdoor/sunny -> sunny
            var parts = weatherId.Split('/');
            if (parts.Length < 2) continue;

            var weatherName = parts[^1];
            var categoryParts = weatherId.Split(':');
            var category = categoryParts.Length > 2 ? categoryParts[2].Split('/')[0] : "outdoor";

            var config = WeatherConfigs.GetValueOrDefault(weatherName, new WeatherConfig(1.0, false));

            var definition = new
            {
                // Primary key
                weatherId,

                // BaseEntity fields
                name = FormatDisplayName(weatherName),
                description = $"{FormatDisplayName(weatherName)} weather condition",

                // Weather properties
                category,
                intensity = config.Intensity,
                affectsBattle = config.AffectsBattle,
                ambientSoundId = config.AmbientSoundId,
                effectScriptId = config.EffectScriptId,
                screenTint = config.ScreenTint,
                screenTintOpacity = config.ScreenTintOpacity,
                reducesVisibility = config.ReducesVisibility,
                visibilityRange = config.VisibilityRange
            };

            var filename = ToPascalCase(weatherName) + ".json";
            var outputPath = Path.Combine(weatherDir, filename);
            if (!File.Exists(outputPath))
            {
                File.WriteAllText(outputPath, JsonSerializer.Serialize(definition, JsonOptions));
                count++;
            }
        }

        return count;
    }

    private int GenerateBattleSceneDefinitions()
    {
        var sceneDir = Path.Combine(_outputPath, "Definitions", "BattleScenes");
        Directory.CreateDirectory(sceneDir);

        int count = 0;
        foreach (var sceneId in _battleSceneIds)
        {
            // base:battlescene:normal/grass -> grass
            var parts = sceneId.Split('/');
            if (parts.Length < 2) continue;

            var sceneName = parts[^1];
            var categoryParts = sceneId.Split(':');
            var category = categoryParts.Length > 2 ? categoryParts[2].Split('/')[0] : "normal";

            var config = BattleSceneConfigs.GetValueOrDefault(sceneName, new BattleSceneConfig(category, sceneName));
            var bgName = config.BackgroundName;

            var definition = new
            {
                // Primary key
                battleSceneId = sceneId,

                // BaseEntity fields
                name = FormatDisplayName(sceneName),
                description = $"{FormatDisplayName(sceneName)} battle background",

                // Battle scene properties
                category = config.Category,
                backgroundTextureId = $"base:texture:battle/background/{bgName}",
                playerPlatformTextureId = $"base:texture:battle/platform/{bgName}_player",
                enemyPlatformTextureId = $"base:texture:battle/platform/{bgName}_enemy",
                paletteId = config.PaletteId,
                defaultMusicId = config.DefaultMusicId,
                hasAnimatedBackground = config.HasAnimatedBackground,
                backgroundAnimationId = (string?)null,
                playerPlatformOffsetY = 0,
                enemyPlatformOffsetY = 0
            };

            var filename = ToPascalCase(sceneName) + ".json";
            var outputPath = Path.Combine(sceneDir, filename);
            if (!File.Exists(outputPath))
            {
                File.WriteAllText(outputPath, JsonSerializer.Serialize(definition, JsonOptions));
                count++;
            }
        }

        return count;
    }

    private bool GenerateRegionDefinition()
    {
        var regionDir = Path.Combine(_outputPath, "Definitions", "Regions");
        Directory.CreateDirectory(regionDir);

        var regionFormatted = char.ToUpper(_region[0]) + _region[1..].ToLower();

        var definition = new
        {
            // Primary key
            regionId = $"base:region:{_region}",

            // BaseEntity fields
            name = regionFormatted,
            displayName = regionFormatted,
            description = $"The {regionFormatted} region",

            // Region properties
            regionMapTextureId = $"base:texture:region/map/{_region}",
            startingMapId = $"base:map:{_region}/littleroot_town",
            startingX = 5,
            startingY = 8,
            startingDirection = "down",
            defaultFlyMapId = $"base:map:{_region}/littleroot_town",
            defaultFlyX = 5,
            defaultFlyY = 8,
            regionalDexId = $"base:pokedex:{_region}/regional",
            sortOrder = _region == "hoenn" ? 3 : 1,
            isPlayable = true
        };

        var filename = ToPascalCase(_region) + ".json";
        var outputPath = Path.Combine(regionDir, filename);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(definition, JsonOptions));
        return true;
    }

    private static string FormatDisplayName(string name)
    {
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }

    private static string ToPascalCase(string name)
    {
        return string.Concat(name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
    }

    private record WeatherConfig(
        double Intensity,
        bool AffectsBattle,
        string? ScreenTint = null,
        double ScreenTintOpacity = 0,
        string? EffectScriptId = null,
        string? AmbientSoundId = null,
        bool ReducesVisibility = false,
        int VisibilityRange = 10
    );

    private record BattleSceneConfig(
        string Category,
        string BackgroundName,
        bool HasAnimatedBackground = false,
        string? PaletteId = null,
        string? DefaultMusicId = null
    );
}
