using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Definition for a font loaded from mod definitions.
    /// Supports both TrueType fonts (via fontPath) and bitmap fonts (via texturePath).
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
        /// Gets or sets the type of definition (should be "Font").
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "Font";

        /// <summary>
        /// Gets or sets the description of the font.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the category of the font (e.g., "game", "debug").
        /// </summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        // ============================================
        // TrueType Font Properties
        // ============================================

        /// <summary>
        /// Gets or sets the path to the TrueType font file relative to the mod root.
        /// </summary>
        [JsonPropertyName("fontPath")]
        public string? FontPath { get; set; }

        /// <summary>
        /// Gets or sets the default size for TrueType fonts in pixels.
        /// </summary>
        [JsonPropertyName("defaultSize")]
        public int DefaultSize { get; set; }

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
        public string? Version { get; set; }

        // ============================================
        // Bitmap Font Properties
        // ============================================

        /// <summary>
        /// Gets or sets the path to the bitmap font texture relative to the mod root.
        /// </summary>
        [JsonPropertyName("texturePath")]
        public string? TexturePath { get; set; }

        /// <summary>
        /// Gets or sets the width of each glyph cell in the texture.
        /// </summary>
        [JsonPropertyName("glyphWidth")]
        public int GlyphWidth { get; set; }

        /// <summary>
        /// Gets or sets the height of each glyph cell in the texture.
        /// </summary>
        [JsonPropertyName("glyphHeight")]
        public int GlyphHeight { get; set; }

        /// <summary>
        /// Gets or sets the number of glyphs per row in the texture.
        /// </summary>
        [JsonPropertyName("glyphsPerRow")]
        public int GlyphsPerRow { get; set; }

        /// <summary>
        /// Gets or sets the ID of the character map definition to use for this font.
        /// </summary>
        [JsonPropertyName("characterMapId")]
        public string? CharacterMapId { get; set; }

        /// <summary>
        /// Gets or sets the width of each glyph for variable-width font rendering.
        /// Index corresponds to glyph index in the texture.
        /// </summary>
        [JsonPropertyName("glyphWidths")]
        public List<int>? GlyphWidths { get; set; }

        /// <summary>
        /// Gets or sets the line height in pixels.
        /// </summary>
        [JsonPropertyName("lineHeight")]
        public int LineHeight { get; set; }

        /// <summary>
        /// Gets or sets the baseline offset from top in pixels.
        /// </summary>
        [JsonPropertyName("baseline")]
        public int Baseline { get; set; }

        /// <summary>
        /// Gets or sets the line spacing multiplier (for TrueType fonts).
        /// </summary>
        [JsonPropertyName("lineSpacing")]
        public float LineSpacing { get; set; }

        /// <summary>
        /// Gets or sets the character spacing in pixels.
        /// </summary>
        [JsonPropertyName("characterSpacing")]
        public float CharacterSpacing { get; set; }

        /// <summary>
        /// Gets a value indicating whether this is a bitmap font.
        /// </summary>
        [JsonIgnore]
        public bool IsBitmapFont => !string.IsNullOrEmpty(TexturePath);

        /// <summary>
        /// Gets a value indicating whether this is a TrueType font.
        /// </summary>
        [JsonIgnore]
        public bool IsTrueTypeFont => !string.IsNullOrEmpty(FontPath);

        /// <summary>
        /// Gets the actual width of a glyph at the specified index.
        /// Returns the cell width if no variable widths are defined.
        /// </summary>
        /// <param name="glyphIndex">The glyph index.</param>
        /// <returns>The width in pixels.</returns>
        public int GetGlyphWidth(int glyphIndex)
        {
            if (GlyphWidths != null && glyphIndex >= 0 && glyphIndex < GlyphWidths.Count)
            {
                return GlyphWidths[glyphIndex];
            }
            return GlyphWidth;
        }
    }
}
