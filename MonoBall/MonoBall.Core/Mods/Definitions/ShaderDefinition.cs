using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods.Definitions
{
    /// <summary>
    /// Definition for a shader effect that can be loaded from mods.
    /// </summary>
    public class ShaderDefinition
    {
        /// <summary>
        /// Gets or sets the unique identifier for the shader (e.g., "base:shader:colorgrading").
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the shader.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the optional description of what the shader does.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the path to the compiled .mgfxo file relative to mod root (e.g., "Shaders/ColorGrading.mgfxo").
        /// The shader must be compiled from .fx to .mgfxo during build.
        /// </summary>
        [JsonPropertyName("sourceFile")]
        public string SourceFile { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the optional list of shader parameters with metadata.
        /// </summary>
        [JsonPropertyName("parameters")]
        public List<ShaderParameterDefinition>? Parameters { get; set; }
    }

    /// <summary>
    /// Definition for a shader parameter.
    /// </summary>
    public class ShaderParameterDefinition
    {
        /// <summary>
        /// Gets or sets the parameter name as defined in the shader (.fx file).
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the parameter type (e.g., "float", "float2", "float3", "float4", "Texture2D").
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the default value for the parameter.
        /// </summary>
        [JsonPropertyName("defaultValue")]
        public object? DefaultValue { get; set; }

        /// <summary>
        /// Gets or sets the optional minimum value (for numeric types).
        /// </summary>
        [JsonPropertyName("min")]
        public double? Min { get; set; }

        /// <summary>
        /// Gets or sets the optional maximum value (for numeric types).
        /// </summary>
        [JsonPropertyName("max")]
        public double? Max { get; set; }
    }
}
