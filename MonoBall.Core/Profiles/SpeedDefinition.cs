using System.Text.Json.Serialization;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Speed definition with animation type mapping.
///     Links a movement type to its speed and which animation type to use.
/// </summary>
public class SpeedDefinition
{
    /// <summary>
    ///     Gets or sets the movement speed in tiles per second.
    /// </summary>
    [JsonPropertyName("speed")]
    public float Speed { get; set; }

    /// <summary>
    ///     Gets or sets the animation type to use for this movement type.
    ///     References an animation type in the animation profile (e.g., "go", "go_fast", "run").
    /// </summary>
    [JsonPropertyName("animationType")]
    public string AnimationType { get; set; } = string.Empty;
}
