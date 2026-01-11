using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Definition for an animation profile loaded from JSON.
///     Defines animation speeds and durations for standardized animation types.
/// </summary>
public class AnimationProfileDefinition
{
    /// <summary>
    ///     Gets or sets the unique identifier for the profile.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the definition type identifier.
    /// </summary>
    [JsonPropertyName("definitionType")]
    public string DefinitionType { get; set; } = "AnimationProfile";

    /// <summary>
    ///     Gets or sets the human-readable name for the profile.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    ///     Gets or sets the optional description of the profile.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    ///     Gets or sets the animation definitions for each animation type.
    ///     Key is animation type (e.g., "face", "go", "go_fast", "run"), value is animation definition.
    /// </summary>
    [JsonPropertyName("animations")]
    public Dictionary<string, AnimationDefinition> Animations { get; set; } = new();

    /// <summary>
    ///     Gets or sets the default animation type to use when not specified.
    ///     Must match a key in <see cref="Animations" />.
    /// </summary>
    [JsonPropertyName("defaultAnimation")]
    public string DefaultAnimation { get; set; } = string.Empty;
}
