using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Definition for a popup background bitmap.
    /// Backgrounds fill the interior of the popup.
    /// </summary>
    public class PopupBackgroundDefinition
    {
        /// <summary>
        /// Gets or sets the unique identifier for the background.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the background.
        /// </summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of background (always "Bitmap").
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "Bitmap";

        /// <summary>
        /// Gets or sets the path to the texture file relative to the mod root.
        /// </summary>
        [JsonPropertyName("texturePath")]
        public string TexturePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the width of the background in pixels.
        /// </summary>
        [JsonPropertyName("width")]
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the background in pixels.
        /// </summary>
        [JsonPropertyName("height")]
        public int Height { get; set; }

        /// <summary>
        /// Gets or sets an optional description of the background.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
