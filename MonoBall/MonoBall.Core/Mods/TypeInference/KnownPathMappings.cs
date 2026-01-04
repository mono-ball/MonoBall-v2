using System.Collections.Generic;
using System.Linq;

namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Pre-sorted path mappings for known definition types (sorted by path length, most specific first).
/// </summary>
public static class KnownPathMappings
{
    /// <summary>
    /// Pre-sorted path mappings array (sorted by path length descending, most specific first).
    /// </summary>
    public static readonly (string Path, string Type)[] SortedMappings = new Dictionary<
        string,
        string
    >
    {
        // Asset definitions (most specific first)
        { "Definitions/Assets/UI/Popups/Backgrounds", "PopupBackgroundAsset" },
        { "Definitions/Assets/UI/Popups/Outlines", "PopupOutlineAsset" },
        { "Definitions/Assets/Maps/Tiles/DoorAnimations", "DoorAnimationAsset" },
        { "Definitions/Assets/Maps/Tilesets", "TilesetAsset" },
        { "Definitions/Assets/UI/Interface", "InterfaceAsset" },
        { "Definitions/Assets/UI/TextWindows", "TextWindowAsset" },
        { "Definitions/Assets/Characters", "CharacterAsset" },
        { "Definitions/Assets/FieldEffects", "FieldEffectAsset" },
        { "Definitions/Assets/Shaders", "ShaderAsset" },
        { "Definitions/Assets/Sprites", "SpriteAsset" },
        { "Definitions/Assets/Weather", "WeatherAsset" },
        { "Definitions/Assets/Audio", "AudioAsset" },
        { "Definitions/Assets/Battle", "BattleAsset" },
        { "Definitions/Assets/Fonts", "FontAsset" },
        { "Definitions/Assets/Objects", "ObjectAsset" },
        { "Definitions/Assets/Pokemon", "PokemonAsset" },
        // Constants (top-level)
        { "Definitions/Constants", "Constants" },
        // Entity definitions (most specific first)
        { "Definitions/Entities/Text/ColorPalettes", "ColorPalette" },
        { "Definitions/Entities/Text/TextEffects", "TextEffect" },
        { "Definitions/Entities/BattleScenes", "BattleScene" },
        { "Definitions/Entities/MapSections", "MapSection" },
        { "Definitions/Entities/PopupThemes", "PopupTheme" },
        { "Definitions/Entities/Maps", "Map" },
        { "Definitions/Entities/Pokemon", "Pokemon" },
        { "Definitions/Entities/Regions", "Region" },
        { "Definitions/Entities/Weather", "Weather" },
        // Scripts (matches all subdirectories: Interactions/, Movement/, Triggers/, etc.)
        { "Definitions/Scripts", "Script" },
    }
        .OrderByDescending(kvp => kvp.Key.Length)
        .Select(kvp => (kvp.Key, kvp.Value))
        .ToArray();
}
