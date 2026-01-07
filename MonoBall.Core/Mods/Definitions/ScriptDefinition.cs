using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods.Definitions;

/// <summary>
///     Definition for a script that can be loaded from mods.
///     Scripts are C# scripts (.csx files) that extend ScriptBase.
/// </summary>
public class ScriptDefinition
{
    /// <summary>
    ///     Gets or sets the unique identifier for the script (e.g., "base:script:behavior/stationary").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the display name of the script.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the optional description of what this script does.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    ///     Gets or sets the path to the .csx script file relative to mod root (e.g., "Scripts/behaviors/stationary.csx").
    /// </summary>
    [JsonPropertyName("scriptPath")]
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the category of the script (e.g., "behavior", "tile", "item").
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    ///     Gets or sets the execution priority (higher = executes first). Default is 500.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 500;

    /// <summary>
    ///     Gets or sets the optional list of script parameters with metadata.
    ///     Parameters are passed to the script instance during initialization.
    /// </summary>
    [JsonPropertyName("parameters")]
    public List<ScriptParameterDefinition>? Parameters { get; set; }
}

/// <summary>
///     Definition for a script parameter.
///     Similar to ShaderParameterDefinition, provides type-safe parameter configuration.
/// </summary>
public class ScriptParameterDefinition
{
    /// <summary>
    ///     Gets or sets the parameter name as used in the script.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the parameter type (e.g., "string", "int", "float", "bool", "Vector2").
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the default value for the parameter.
    /// </summary>
    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }

    /// <summary>
    ///     Gets or sets the optional minimum value (for numeric types).
    /// </summary>
    [JsonPropertyName("min")]
    public double? Min { get; set; }

    /// <summary>
    ///     Gets or sets the optional maximum value (for numeric types).
    /// </summary>
    [JsonPropertyName("max")]
    public double? Max { get; set; }

    /// <summary>
    ///     Gets or sets the optional description of the parameter.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
