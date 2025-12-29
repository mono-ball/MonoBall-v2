using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods.Definitions;

/// <summary>
///     Definition for a shader preset that stores a saved shader configuration.
///     Presets are loaded from JSON files in the ShaderPresets folder.
/// </summary>
public class ShaderPresetDefinition
{
    /// <summary>
    ///     Gets or sets the unique identifier for the preset (e.g., "base:shaderpreset:spooky_night").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name of the preset.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    ///     Gets or sets the description of what effect this preset creates.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    ///     Gets or sets the shader ID this preset applies to (e.g., "base:shader:showcase").
    /// </summary>
    [JsonPropertyName("shaderId")]
    public string ShaderId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the parameter values to apply.
    ///     Keys are parameter names, values are the parameter values.
    ///     Supported value types: number, array (for vectors), object (for colors).
    /// </summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }

    /// <summary>
    ///     Gets or sets optional tags for categorizing presets.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>
    ///     Gets or sets the author of this preset.
    /// </summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }
}
