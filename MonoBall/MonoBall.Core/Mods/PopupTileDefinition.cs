using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Defines an individual tile in a popup outline tile sheet.
    /// </summary>
    public class PopupTileDefinition
    {
        /// <summary>
        /// Gets or sets the tile index within the sheet.
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>
        /// Gets or sets the X position of the tile in the texture.
        /// </summary>
        [JsonPropertyName("x")]
        public int X { get; set; }

        /// <summary>
        /// Gets or sets the Y position of the tile in the texture.
        /// </summary>
        [JsonPropertyName("y")]
        public int Y { get; set; }

        /// <summary>
        /// Gets or sets the width of the tile in pixels.
        /// </summary>
        [JsonPropertyName("width")]
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the tile in pixels.
        /// </summary>
        [JsonPropertyName("height")]
        public int Height { get; set; }
    }
}
