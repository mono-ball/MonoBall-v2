using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoBall.Core.Mods;

namespace MonoBall.Core.TextEffects
{
    /// <summary>
    /// Calculates text effect transformations for per-character rendering.
    /// </summary>
    public class TextEffectCalculator : ITextEffectCalculator
    {
        /// <inheritdoc/>
        public Vector2 CalculatePositionOffset(
            TextEffectDefinition effect,
            int charIndex,
            float totalTime,
            Vector2 shakeOffset
        )
        {
            float x = 0f;
            float y = 0f;
            float charPhase = charIndex * effect.WavePhaseOffset;

            // Wave: vertical sine wave
            if (effect.EffectTypes.HasFlag(TextEffectType.Wave))
            {
                y +=
                    MathF.Sin((totalTime * effect.WaveFrequency) + charPhase)
                    * effect.WaveAmplitude;
            }

            // Hang: vertical bounce (absolute sine)
            if (effect.EffectTypes.HasFlag(TextEffectType.Hang))
            {
                y +=
                    MathF.Abs(MathF.Sin((totalTime * effect.HangFrequency) + charPhase))
                    * effect.HangAmplitude;
            }

            // Side step: horizontal oscillation
            if (effect.EffectTypes.HasFlag(TextEffectType.SideStep))
            {
                x +=
                    MathF.Sin((totalTime * effect.SideStepFrequency) + charPhase)
                    * effect.SideStepAmplitude;
            }

            // Shake: add pre-calculated offset
            if (effect.EffectTypes.HasFlag(TextEffectType.Shake))
            {
                x += shakeOffset.X;
                y += shakeOffset.Y;
            }

            return new Vector2(x, y);
        }

        /// <inheritdoc/>
        public Color CalculateCycleColor(
            ColorPaletteDefinition palette,
            TextEffectDefinition effect,
            int charIndex,
            float totalTime,
            float cycleSpeed
        )
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }
            if (effect == null)
            {
                throw new ArgumentNullException(nameof(effect));
            }

            var colors = palette.GetColors();
            if (colors.Length == 0)
            {
                throw new InvalidOperationException(
                    $"ColorPalette '{palette.Id}' has no colors defined."
                );
            }

            if (colors.Length == 1)
            {
                return colors[0];
            }

            // Use effect definition's color phase offset (falls back to wave phase offset if not specified)
            float phase = (totalTime * cycleSpeed) + (charIndex * effect.EffectiveColorPhaseOffset);
            float normalizedPhase = phase - MathF.Floor(phase); // 0.0 to 1.0

            if (!palette.Interpolate)
            {
                // Snap to nearest color
                int colorIndex = (int)(normalizedPhase * colors.Length) % colors.Length;
                return colors[colorIndex];
            }

            // Interpolate between colors
            float scaledPhase = normalizedPhase * colors.Length;
            int index1 = (int)scaledPhase % colors.Length;
            int index2 = (index1 + 1) % colors.Length;
            float t = scaledPhase - MathF.Floor(scaledPhase);

            return Color.Lerp(colors[index1], colors[index2], t);
        }

        /// <inheritdoc/>
        public Dictionary<int, Vector2> GenerateShakeOffsets(
            TextEffectDefinition effect,
            int charCount,
            int seed
        )
        {
            var offsets = new Dictionary<int, Vector2>(charCount);
            var random = new Random(seed);

            for (int i = 0; i < charCount; i++)
            {
                float x = ((float)random.NextDouble() * 2f - 1f) * effect.ShakeStrength;
                float y = ((float)random.NextDouble() * 2f - 1f) * effect.ShakeStrength;
                offsets[i] = new Vector2(x, y);
            }

            return offsets;
        }

        /// <inheritdoc/>
        public float CalculateRotation(TextEffectDefinition effect, int charIndex, float totalTime)
        {
            if (!effect.EffectTypes.HasFlag(TextEffectType.Wobble))
            {
                return 0f;
            }

            // Phase offset per character for staggered wobble (uses per-effect offset)
            float charPhase = charIndex * effect.EffectiveWobblePhaseOffset;

            // Calculate rotation in radians (amplitude is in degrees)
            float rotationDegrees =
                MathF.Sin((totalTime * effect.WobbleFrequency) + charPhase)
                * effect.WobbleAmplitude;

            // Convert to radians
            return rotationDegrees * (MathF.PI / 180f);
        }

        /// <inheritdoc/>
        public float CalculateScale(TextEffectDefinition effect, int charIndex, float totalTime)
        {
            if (!effect.EffectTypes.HasFlag(TextEffectType.Scale))
            {
                return 1f;
            }

            // Phase offset per character for staggered scaling (uses per-effect offset)
            float charPhase = charIndex * effect.EffectiveScalePhaseOffset;

            // Oscillate between min and max scale
            // Sin returns -1 to 1, normalize to 0 to 1
            float normalizedSin =
                (MathF.Sin((totalTime * effect.ScaleFrequency) + charPhase) + 1f) * 0.5f;

            // Lerp between min and max scale
            return effect.ScaleMin + (normalizedSin * (effect.ScaleMax - effect.ScaleMin));
        }

        /// <inheritdoc/>
        public float CalculateOpacity(TextEffectDefinition effect, int charIndex, float totalTime)
        {
            if (!effect.EffectTypes.HasFlag(TextEffectType.Fade))
            {
                return 1f;
            }

            // Phase offset per character for staggered fading (uses per-effect offset)
            float charPhase = charIndex * effect.EffectiveFadePhaseOffset;

            // Oscillate between min and max opacity
            // Sin returns -1 to 1, normalize to 0 to 1
            float normalizedSin =
                (MathF.Sin((totalTime * effect.FadeFrequency) + charPhase) + 1f) * 0.5f;

            // Lerp between min and max opacity
            return effect.FadeMin + (normalizedSin * (effect.FadeMax - effect.FadeMin));
        }

        /// <inheritdoc/>
        public float CalculateGlowOpacity(
            TextEffectDefinition effect,
            int charIndex,
            float totalTime
        )
        {
            if (!effect.EffectTypes.HasFlag(TextEffectType.Glow))
            {
                return 0f;
            }

            float baseOpacity = effect.GlowOpacity;

            // If glow pulses, modulate with fade parameters
            if (effect.GlowPulses && effect.FadeFrequency > 0)
            {
                float charPhase = charIndex * effect.EffectiveFadePhaseOffset;
                float normalizedSin =
                    (MathF.Sin((totalTime * effect.FadeFrequency) + charPhase) + 1f) * 0.5f;
                // Pulse between 0 and base opacity
                return baseOpacity * normalizedSin;
            }

            return baseOpacity;
        }
    }
}
