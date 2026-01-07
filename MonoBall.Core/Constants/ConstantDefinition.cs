using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Constants;

/// <summary>
///     Represents a constants definition loaded from a mod.
///     Contains multiple constants grouped together (e.g., all game constants or all message box constants).
/// </summary>
public class ConstantDefinition
{
    /// <summary>
    ///     Gets or sets the unique identifier of this constants definition.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the dictionary of constant keys to their JSON values.
    ///     Each key-value pair represents one constant (e.g., "TileChunkSize": 16).
    /// </summary>
    [JsonPropertyName("constants")]
    public Dictionary<string, JsonElement> Constants { get; set; } = new();

    /// <summary>
    ///     Gets or sets the optional validation rules for constants.
    ///     Maps constant keys to their validation constraints (min/max values).
    ///     Follows the same pattern as ScriptParameterDefinition and ShaderParameterDefinition.
    /// </summary>
    [JsonPropertyName("validationRules")]
    public Dictionary<string, ConstantValidationRule>? ValidationRules { get; set; }
}
