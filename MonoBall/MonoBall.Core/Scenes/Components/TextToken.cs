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
    }
}
