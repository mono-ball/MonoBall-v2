using System.Text.Json.Serialization;

namespace MonoBall.Core.Maps
{
    /// <summary>
    /// Represents a reference to a tileset used in a map.
    /// </summary>
    public class TilesetReference
    {
        /// <summary>
        /// The first Global ID (GID) for this tileset.
        /// </summary>
        [JsonPropertyName("firstGid")]
        public int FirstGid { get; set; }

        /// <summary>
        /// The tileset ID.
        /// </summary>
        [JsonPropertyName("tilesetId")]
        public string TilesetId { get; set; } = string.Empty;
    }
}
