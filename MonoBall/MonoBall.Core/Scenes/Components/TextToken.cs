using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Represents a parsed text token (character or control code).
    /// </summary>
    public struct TextToken
    {
        /// <summary>
        /// The type of token.
        /// </summary>
        public TextTokenType TokenType { get; set; }

        /// <summary>
        /// The value of the token (character, pause frames, color values, etc.).
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// The original position in the text string.
        /// </summary>
        public int OriginalPosition { get; set; }

        /// <summary>
        /// Gets the pause duration in seconds if this is a Pause token.
        /// </summary>
        /// <returns>The pause duration in seconds, or null if not a Pause token.</returns>
        public float? GetPauseSeconds()
        {
            return TokenType == TextTokenType.Pause ? (float?)Value : null;
        }

        /// <summary>
        /// Gets the color value if this is a Color token.
        /// </summary>
        /// <returns>The color, or null if not a Color token.</returns>
        public Color? GetColor()
        {
            return TokenType == TextTokenType.Color ? (Color?)Value : null;
        }

        /// <summary>
        /// Gets the shadow color value if this is a Shadow token.
        /// </summary>
        /// <returns>The shadow color, or null if not a Shadow token.</returns>
        public Color? GetShadowColor()
        {
            return TokenType == TextTokenType.Shadow ? (Color?)Value : null;
        }

        /// <summary>
        /// Gets the speed value if this is a Speed token.
        /// </summary>
        /// <returns>The speed, or null if not a Speed token.</returns>
        public int? GetSpeed()
        {
            return TokenType == TextTokenType.Speed ? (int?)Value : null;
        }

        /// <summary>
        /// Gets the effect ID if this is an EffectStart token.
        /// </summary>
        /// <returns>The effect definition ID, or null if not an EffectStart token.</returns>
        public string? GetEffectId()
        {
            return TokenType == TextTokenType.EffectStart ? (string?)Value : null;
        }
    }
}
