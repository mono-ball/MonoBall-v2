using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoBall.Core.Mods;

namespace MonoBall.Core.TextEffects
{
    /// <summary>
    /// Calculates text effect transformations for per-character rendering.
    /// </summary>
    public interface ITextEffectCalculator
    {
        /// <summary>
        /// Calculates the position offset for a character based on effect parameters.
        /// </summary>
        /// <param name="effect">The effect definition.</param>
        /// <param name="charIndex">Character index in line (for phase offset).</param>
        /// <param name="totalTime">Total elapsed time in seconds.</param>
        /// <param name="shakeOffset">Pre-calculated shake offset (if applicable).</param>
        /// <returns>Position offset in pixels (unscaled).</returns>
        Vector2 CalculatePositionOffset(
            TextEffectDefinition effect,
            int charIndex,
            float totalTime,
            Vector2 shakeOffset
        );

        /// <summary>
        /// Calculates the color for a character with color cycling.
        /// </summary>
        /// <param name="palette">The color palette definition.</param>
        /// <param name="effect">The text effect definition (for phase offset configuration).</param>
        /// <param name="charIndex">Character index in line (for phase offset).</param>
        /// <param name="totalTime">Total elapsed time in seconds.</param>
        /// <param name="cycleSpeed">Color cycle speed.</param>
        /// <returns>The calculated color.</returns>
        Color CalculateCycleColor(
            ColorPaletteDefinition palette,
            TextEffectDefinition effect,
            int charIndex,
            float totalTime,
            float cycleSpeed
        );

        /// <summary>
        /// Generates shake offsets for a range of characters.
        /// Called when shake interval elapses to create new random positions.
        /// </summary>
        /// <param name="effect">The effect definition.</param>
        /// <param name="charCount">Number of characters to generate offsets for.</param>
        /// <param name="seed">Seed for deterministic randomness.</param>
        /// <returns>Dictionary mapping character index to offset.</returns>
        Dictionary<int, Vector2> GenerateShakeOffsets(
            TextEffectDefinition effect,
            int charCount,
            int seed
        );

        /// <summary>
        /// Calculates the rotation angle for a character with wobble effect.
        /// </summary>
        /// <param name="effect">The effect definition.</param>
        /// <param name="charIndex">Character index in line (for phase offset).</param>
        /// <param name="totalTime">Total elapsed time in seconds.</param>
        /// <returns>Rotation angle in radians.</returns>
        float CalculateRotation(TextEffectDefinition effect, int charIndex, float totalTime);

        /// <summary>
        /// Calculates the scale factor for a character with scale effect.
        /// </summary>
        /// <param name="effect">The effect definition.</param>
        /// <param name="charIndex">Character index in line (for phase offset).</param>
        /// <param name="totalTime">Total elapsed time in seconds.</param>
        /// <returns>Scale factor (1.0 = normal size).</returns>
        float CalculateScale(TextEffectDefinition effect, int charIndex, float totalTime);

        /// <summary>
        /// Calculates the opacity for a character with fade effect.
        /// </summary>
        /// <param name="effect">The effect definition.</param>
        /// <param name="charIndex">Character index in line (for phase offset).</param>
        /// <param name="totalTime">Total elapsed time in seconds.</param>
        /// <returns>Opacity value (0.0 = invisible, 1.0 = fully visible).</returns>
        float CalculateOpacity(TextEffectDefinition effect, int charIndex, float totalTime);

        /// <summary>
        /// Calculates the glow opacity for a character with glow effect.
        /// </summary>
        /// <param name="effect">The effect definition.</param>
        /// <param name="charIndex">Character index in line (for phase offset).</param>
        /// <param name="totalTime">Total elapsed time in seconds.</param>
        /// <returns>Glow opacity value (0.0 - 1.0).</returns>
        float CalculateGlowOpacity(TextEffectDefinition effect, int charIndex, float totalTime);
    }
}
