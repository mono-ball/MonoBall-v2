using System.Text.Json.Serialization;

namespace MonoBall.Core.Maps
{
    /// <summary>
    /// Represents a map section (MAPSEC) definition with popup theme reference.
    /// Map sections define region map areas and which popup theme to use.
    /// </summary>
    public class MapSectionDefinition
    {
        /// <summary>
        /// Gets or sets the unique MAPSEC identifier (e.g., "MAPSEC_LITTLEROOT_TOWN").
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name for the region map (e.g., "LITTLEROOT TOWN").
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the popup theme ID reference (e.g., "wood", "marble", "stone").
        /// </summary>
        [JsonPropertyName("theme")]
        public string PopupTheme { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the X position on the region map grid (in 8x8 pixel tiles).
        /// </summary>
        [JsonPropertyName("x")]
        public int? X { get; set; }

        /// <summary>
        /// Gets or sets the Y position on the region map grid (in 8x8 pixel tiles).
        /// </summary>
        [JsonPropertyName("y")]
        public int? Y { get; set; }

        /// <summary>
        /// Gets or sets the width on the region map (in tiles).
        /// </summary>
        [JsonPropertyName("width")]
        public int? Width { get; set; }

        /// <summary>
        /// Gets or sets the height on the region map (in tiles).
        /// </summary>
        [JsonPropertyName("height")]
        public int? Height { get; set; }
    }
}
