using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods;

/// <summary>
///     Definition for a popup theme that links a background and outline together.
/// </summary>
public class PopupThemeDefinition
{
    /// <summary>
    ///     Gets or sets the unique identifier for the theme.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name of the theme.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets an optional description of the theme.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    ///     Gets or sets the background definition ID to use.
    /// </summary>
    [JsonPropertyName("background")]
    public string Background { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the outline definition ID to use.
    /// </summary>
    [JsonPropertyName("outline")]
    public string Outline { get; set; } = string.Empty;
}
