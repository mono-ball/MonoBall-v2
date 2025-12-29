using System.Text.Json.Serialization;

namespace MonoBall.Core.Maps;

/// <summary>
///     Represents a connection to another map.
/// </summary>
public class MapConnection
{
    /// <summary>
    ///     The target map ID.
    /// </summary>
    [JsonPropertyName("mapId")]
    public string MapId { get; set; } = string.Empty;

    /// <summary>
    ///     The offset in tiles from the current map.
    /// </summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; }
}
