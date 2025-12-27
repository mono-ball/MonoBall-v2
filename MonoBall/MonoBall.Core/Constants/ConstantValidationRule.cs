using System.Text.Json.Serialization;

namespace MonoBall.Core.Constants
{
    /// <summary>
    /// Validation rule for a constant value.
    /// Similar to ScriptParameterDefinition and ShaderParameterDefinition, provides min/max validation.
    /// Note: Unlike Script/Shader parameters (which are objects with inline min/max properties),
    /// constants are primitive values, so validation rules are defined separately in a dictionary.
    /// </summary>
    public class ConstantValidationRule
    {
        /// <summary>
        /// Gets or sets the optional minimum value (for numeric constants).
        /// </summary>
        [JsonPropertyName("min")]
        public double? Min { get; set; }

        /// <summary>
        /// Gets or sets the optional maximum value (for numeric constants).
        /// </summary>
        [JsonPropertyName("max")]
        public double? Max { get; set; }

        /// <summary>
        /// Gets or sets the optional description of the validation rule.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
