using System.Collections.Generic;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.Mods;

/// <summary>
///     Represents a color in JSON format for palette definitions.
/// </summary>
public class PaletteColor
{
    /// <summary>
    ///     Gets or sets the red component (0-255).
    /// </summary>
    [JsonPropertyName("r")]
    public byte R { get; set; }

    /// <summary>
    ///     Gets or sets the green component (0-255).
    /// </summary>
    [JsonPropertyName("g")]
    public byte G { get; set; }

    /// <summary>
    ///     Gets or sets the blue component (0-255).
    /// </summary>
    [JsonPropertyName("b")]
    public byte B { get; set; }

    /// <summary>
    ///     Gets or sets the alpha component (0-255). Defaults to 255 (opaque).
    /// </summary>
    [JsonPropertyName("a")]
    public byte A { get; set; } = 255;

    /// <summary>
    ///     Converts to XNA Color.
    /// </summary>
    /// <returns>The XNA Color value.</returns>
    public Color ToColor()
    {
        return new Color(R, G, B, A);
    }
}

/// <summary>
///     Definition for a color palette used by text effects.
///     Palettes define a sequence of colors for ColorCycle effects.
/// </summary>
public class ColorPaletteDefinition
{
    /// <summary>
    ///     Gets or sets the unique identifier for the palette.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name of the palette.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the type of definition (should be "ColorPalette").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ColorPalette";

    /// <summary>
    ///     Gets or sets the description of the palette.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    ///     Gets or sets the colors in the palette (cycled through in order).
    /// </summary>
    [JsonPropertyName("colors")]
    public List<PaletteColor> Colors { get; set; } = new();

    /// <summary>
    ///     Gets or sets whether to interpolate between colors (true) or snap (false).
    /// </summary>
    [JsonPropertyName("interpolate")]
    public bool Interpolate { get; set; } = true;

    /// <summary>
    ///     Gets or sets the version of the palette definition.
    /// </summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    /// <summary>
    ///     Gets the colors as XNA Color array.
    /// </summary>
    /// <returns>Array of XNA Colors.</returns>
    public Color[] GetColors()
    {
        var result = new Color[Colors.Count];
        for (var i = 0; i < Colors.Count; i++)
            result[i] = Colors[i].ToColor();
        return result;
    }
}
