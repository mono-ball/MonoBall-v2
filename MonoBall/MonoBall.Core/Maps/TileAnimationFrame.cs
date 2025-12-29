using System.Text.Json.Serialization;

namespace MonoBall.Core.Maps;

/// <summary>
///     Represents a single frame in a tile animation.
/// </summary>
public class TileAnimationFrame
{
    /// <summary>
    ///     The tile ID for this animation frame.
    /// </summary>
    [JsonPropertyName("tileId")]
    public int TileId { get; set; }

    /// <summary>
    ///     The duration of this frame in milliseconds.
    /// </summary>
    [JsonPropertyName("durationMs")]
    public int DurationMs { get; set; }
}
