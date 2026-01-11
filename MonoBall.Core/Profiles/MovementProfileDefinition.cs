using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Definition for a movement profile loaded from JSON.
///     Links movement types (walk, run, bike) to speeds and animation types.
/// </summary>
public class MovementProfileDefinition
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
    public string DefinitionType { get; set; } = "MovementProfile";

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
    ///     Gets or sets the movement speeds with animation type mapping.
    ///     Key is movement type (e.g., "walk", "run", "bike"), value is speed definition.
    /// </summary>
    [JsonPropertyName("speeds")]
    public Dictionary<string, SpeedDefinition> Speeds { get; set; } = new();

    /// <summary>
    ///     Gets or sets the default movement type to use when not specified.
    ///     Must match a key in <see cref="Speeds" />.
    /// </summary>
    [JsonPropertyName("defaultSpeed")]
    public string DefaultSpeed { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets optional validation rules for speed values.
    /// </summary>
    [JsonPropertyName("validationRules")]
    public Dictionary<string, SpeedValidationRule>? ValidationRules { get; set; }
}
