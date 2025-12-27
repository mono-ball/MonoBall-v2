using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Definition for a character map that maps characters to glyph indices in a bitmap font.
    /// </summary>
    public class CharacterMapDefinition
    {
        /// <summary>
        /// Gets or sets the unique identifier for the character map.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the character map.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of definition (should be "CharacterMap").
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "CharacterMap";

        /// <summary>
        /// Gets or sets the mappings from character strings to glyph index hex strings.
        /// Key is the character (e.g., "A"), value is the hex index (e.g., "0xBB").
        /// </summary>
        [JsonPropertyName("mappings")]
        public Dictionary<string, string> Mappings { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets or sets special multi-glyph symbols.
        /// Key is the symbol name (e.g., "PKMN"), value is array of hex indices.
        /// </summary>
        [JsonPropertyName("specialSymbols")]
        public Dictionary<string, string[]>? SpecialSymbols { get; set; }

        // Cached parsed mappings for runtime use
        private Dictionary<char, int>? _parsedMappings;
        private Dictionary<string, int[]>? _parsedSpecialSymbols;

        /// <summary>
        /// Gets the glyph index for a character.
        /// </summary>
        /// <param name="c">The character to look up.</param>
        /// <returns>The glyph index, or -1 if not found.</returns>
        public int GetGlyphIndex(char c)
        {
            EnsureParsed();
            return _parsedMappings!.TryGetValue(c, out var index) ? index : -1;
        }

        /// <summary>
        /// Gets the glyph indices for a special symbol.
        /// </summary>
        /// <param name="symbolName">The symbol name (e.g., "PKMN").</param>
        /// <returns>Array of glyph indices, or null if not found.</returns>
        public int[]? GetSpecialSymbol(string symbolName)
        {
            EnsureParsed();
            return _parsedSpecialSymbols?.TryGetValue(symbolName, out var indices) == true
                ? indices
                : null;
        }

        /// <summary>
        /// Checks if a character is mapped in this character map.
        /// </summary>
        /// <param name="c">The character to check.</param>
        /// <returns>True if the character has a mapping.</returns>
        public bool HasMapping(char c)
        {
            EnsureParsed();
            return _parsedMappings!.ContainsKey(c);
        }

        /// <summary>
        /// Gets all mapped characters.
        /// </summary>
        /// <returns>Enumerable of all mapped characters.</returns>
        public IEnumerable<char> GetMappedCharacters()
        {
            EnsureParsed();
            return _parsedMappings!.Keys;
        }

        private void EnsureParsed()
        {
            if (_parsedMappings != null)
                return;

            _parsedMappings = new Dictionary<char, int>();
            foreach (var (key, value) in Mappings)
            {
                if (key.Length == 1)
                {
                    var index = ParseHexValue(value);
                    if (index >= 0)
                    {
                        _parsedMappings[key[0]] = index;
                    }
                }
            }

            if (SpecialSymbols != null)
            {
                _parsedSpecialSymbols = new Dictionary<string, int[]>();
                foreach (var (key, values) in SpecialSymbols)
                {
                    var indices = new List<int>();
                    foreach (var v in values)
                    {
                        var idx = ParseHexValue(v);
                        if (idx >= 0)
                            indices.Add(idx);
                    }
                    if (indices.Count > 0)
                    {
                        _parsedSpecialSymbols[key] = indices.ToArray();
                    }
                }
            }
        }

        private static int ParseHexValue(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return -1;

            var trimmed = hex.Trim();
            if (trimmed.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[2..];
            }

            return int.TryParse(
                trimmed,
                System.Globalization.NumberStyles.HexNumber,
                null,
                out var value
            )
                ? value
                : -1;
        }
    }
}
