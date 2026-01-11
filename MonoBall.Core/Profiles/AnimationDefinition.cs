using System.Text.Json.Serialization;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Animation definition for a specific animation type.
///     Contains duration information and optional per-frame sequences.
///     All durations are in seconds (MonoBall's standard time unit).
/// </summary>
public class AnimationDefinition
{
    /// <summary>
    ///     Gets or sets the base duration per frame in seconds.
    ///     Used for all frames unless <see cref="FrameSequence" /> is specified.
    /// </summary>
    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    /// <summary>
    ///     Gets or sets optional per-frame durations in seconds (overrides <see cref="Duration" /> for each frame).
    ///     If specified, length must match the number of frames in the animation.
    ///     Values are in seconds (same unit as <see cref="Duration" />).
    /// </summary>
    [JsonPropertyName("frameSequence")]
    public double[]? FrameSequence { get; set; }

    /// <summary>
    ///     Gets or sets the optional description of the animation type.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
