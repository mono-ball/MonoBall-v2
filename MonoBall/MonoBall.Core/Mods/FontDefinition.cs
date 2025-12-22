using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Definition for a font loaded from mod definitions.
    /// </summary>
    public class FontDefinition
    {
        /// <summary>
        /// Gets or sets the unique identifier for the font.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the font.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the description of the font.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path to the font file relative to the mod root.
        /// </summary>
        [JsonPropertyName("fontPath")]
        public string FontPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the category of the font (e.g., "game", "debug").
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the default size for the font in pixels.
        /// </summary>
        [JsonPropertyName("defaultSize")]
        public int DefaultSize { get; set; }

        /// <summary>
        /// Gets or sets the line spacing multiplier.
        /// </summary>
        [JsonPropertyName("lineSpacing")]
        public float LineSpacing { get; set; }

        /// <summary>
        /// Gets or sets the character spacing in pixels.
        /// </summary>
        [JsonPropertyName("characterSpacing")]
        public float CharacterSpacing { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the font supports Unicode characters.
        /// </summary>
        [JsonPropertyName("supportsUnicode")]
        public bool SupportsUnicode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the font is monospace.
        /// </summary>
        [JsonPropertyName("isMonospace")]
        public bool IsMonospace { get; set; }

        /// <summary>
        /// Gets or sets the version of the font definition.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }
}
