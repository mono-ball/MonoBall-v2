using System.Text.Json.Serialization;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Validation rule for speed values.
///     Provides min/max validation similar to ConstantValidationRule.
/// </summary>
public class SpeedValidationRule
{
    /// <summary>
    ///     Gets or sets the minimum allowed speed value.
    /// </summary>
    [JsonPropertyName("min")]
    public float? Min { get; set; }

    /// <summary>
    ///     Gets or sets the maximum allowed speed value.
    /// </summary>
    [JsonPropertyName("max")]
    public float? Max { get; set; }

    /// <summary>
    ///     Gets or sets the description of the validation rule.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
