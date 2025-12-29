using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods.Definitions;

/// <summary>
///     Definition for an NPC behavior that references a script and can override script parameters.
///     Behaviors are loaded from Definitions/Behaviors/*.json files.
/// </summary>
public class BehaviorDefinition
{
    /// <summary>
    ///     Gets or sets the unique identifier for the behavior (e.g., "base:behavior:movement/wander").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name of the behavior.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the optional description of what this behavior does.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    ///     Gets or sets the script definition ID that this behavior references (e.g., "base:script:movement/wander").
    ///     This must reference a valid ScriptDefinition.
    /// </summary>
    [JsonPropertyName("scriptId")]
    public string ScriptId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the parameter definitions for this behavior.
    ///     These define what parameters the behavior accepts and their defaults.
    /// </summary>
    [JsonPropertyName("parameters")]
    public List<ScriptParameterDefinition>? Parameters { get; set; }

    /// <summary>
    ///     Gets or sets the optional parameter overrides for the referenced script.
    ///     These override the default parameter values from ScriptDefinition.
    /// </summary>
    [JsonPropertyName("parameterOverrides")]
    public Dictionary<string, object>? ParameterOverrides { get; set; }
}
