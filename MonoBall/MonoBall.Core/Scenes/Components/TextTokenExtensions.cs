using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Extension methods for TextToken to extract typed values.
    /// Separates behavior from data structure (TextToken remains pure data).
    /// </summary>
    public static class TextTokenExtensions
    {
        /// <summary>
        /// Gets the pause duration in seconds if this is a Pause token.
        /// </summary>
        /// <param name="token">The text token.</param>
        /// <returns>The pause duration in seconds, or null if not a Pause token.</returns>
        public static float? GetPauseSeconds(this TextToken token)
        {
            return token.TokenType == TextTokenType.Pause ? (float?)token.Value : null;
        }

        /// <summary>
        /// Gets the color value if this is a Color token.
        /// </summary>
        /// <param name="token">The text token.</param>
        /// <returns>The color, or null if not a Color token.</returns>
        public static Color? GetColor(this TextToken token)
        {
            return token.TokenType == TextTokenType.Color ? (Color?)token.Value : null;
        }

        /// <summary>
        /// Gets the shadow color value if this is a Shadow token.
        /// </summary>
        /// <param name="token">The text token.</param>
        /// <returns>The shadow color, or null if not a Shadow token.</returns>
        public static Color? GetShadowColor(this TextToken token)
        {
            return token.TokenType == TextTokenType.Shadow ? (Color?)token.Value : null;
        }

        /// <summary>
        /// Gets the speed value if this is a Speed token.
        /// </summary>
        /// <param name="token">The text token.</param>
        /// <returns>The speed, or null if not a Speed token.</returns>
        public static int? GetSpeed(this TextToken token)
        {
            return token.TokenType == TextTokenType.Speed ? (int?)token.Value : null;
        }
    }
}
