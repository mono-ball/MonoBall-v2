using System.Collections.Generic;
using System.Text.Json.Serialization;
using MonoBall.Core.Mods;

namespace MonoBall.Core.TextEffects
{
    /// <summary>
    /// Definition for a text effect loaded from mod definitions.
    /// Supports motion effects (wave, shake, hang, sidestep) and color cycling.
    /// </summary>
    public class TextEffectDefinition
    {
        /// <summary>
        /// Gets or sets the unique identifier for the effect.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the effect.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of definition (should be "TextEffect").
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "TextEffect";

        /// <summary>
        /// Gets or sets the description of the effect.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the effect types to apply (can combine multiple).
        /// Parsed from string array in JSON (e.g., ["Wave", "Shake"]).
        /// </summary>
        [JsonPropertyName("effectTypes")]
        public List<string> EffectTypeNames { get; set; } = new();

        /// <summary>
        /// Gets the parsed effect types as flags enum.
        /// </summary>
        [JsonIgnore]
        public TextEffectType EffectTypes
        {
            get
            {
                var result = TextEffectType.None;
                foreach (var name in EffectTypeNames)
                {
                    if (
                        System.Enum.TryParse<TextEffectType>(name, ignoreCase: true, out var parsed)
                    )
                    {
                        result |= parsed;
                    }
                }
                return result;
            }
        }

        // ============================================
        // Wave Effect Parameters
        // ============================================

        /// <summary>
        /// Gets or sets the wave oscillation frequency (higher = faster).
        /// Typical range: 0.5 - 20.0
        /// </summary>
        [JsonPropertyName("waveFrequency")]
        public float WaveFrequency { get; set; }

        /// <summary>
        /// Gets or sets the wave amplitude in pixels (vertical displacement).
        /// Typical range: 0.5 - 10.0
        /// </summary>
        [JsonPropertyName("waveAmplitude")]
        public float WaveAmplitude { get; set; }

        /// <summary>
        /// Gets or sets the phase offset multiplier per character.
        /// 0 = all chars sync, 1 = full wave offset between adjacent chars.
        /// Typical range: 0.0 - 1.0
        /// </summary>
        [JsonPropertyName("wavePhaseOffset")]
        public float WavePhaseOffset { get; set; }

        // ============================================
        // Shake Effect Parameters
        // ============================================

        /// <summary>
        /// Gets or sets the maximum shake displacement in pixels.
        /// Typical range: 0.5 - 5.0
        /// </summary>
        [JsonPropertyName("shakeStrength")]
        public float ShakeStrength { get; set; }

        /// <summary>
        /// Gets or sets the time between shake offset changes in seconds.
        /// Lower = more frantic. Typical range: 0.02 - 0.2
        /// </summary>
        [JsonPropertyName("shakeIntervalSeconds")]
        public float ShakeIntervalSeconds { get; set; }

        // ============================================
        // Hang Effect Parameters
        // ============================================

        /// <summary>
        /// Gets or sets the hang bounce frequency (higher = faster).
        /// Typical range: 1.0 - 15.0
        /// </summary>
        [JsonPropertyName("hangFrequency")]
        public float HangFrequency { get; set; }

        /// <summary>
        /// Gets or sets the hang bounce amplitude in pixels.
        /// Typical range: 1.0 - 8.0
        /// </summary>
        [JsonPropertyName("hangAmplitude")]
        public float HangAmplitude { get; set; }

        // ============================================
        // SideStep Effect Parameters
        // ============================================

        /// <summary>
        /// Gets or sets the side step oscillation frequency.
        /// Typical range: 0.5 - 20.0
        /// </summary>
        [JsonPropertyName("sideStepFrequency")]
        public float SideStepFrequency { get; set; }

        /// <summary>
        /// Gets or sets the side step amplitude in pixels (horizontal displacement).
        /// Typical range: 0.5 - 5.0
        /// </summary>
        [JsonPropertyName("sideStepAmplitude")]
        public float SideStepAmplitude { get; set; }

        // ============================================
        // Wobble Effect Parameters (Character Rotation)
        // ============================================

        /// <summary>
        /// Gets or sets the wobble rotation frequency (higher = faster).
        /// Typical range: 1.0 - 15.0
        /// </summary>
        [JsonPropertyName("wobbleFrequency")]
        public float WobbleFrequency { get; set; }

        /// <summary>
        /// Gets or sets the wobble rotation amplitude in degrees.
        /// Typical range: 5.0 - 45.0
        /// </summary>
        [JsonPropertyName("wobbleAmplitude")]
        public float WobbleAmplitude { get; set; }

        // ============================================
        // Scale Effect Parameters
        // ============================================

        /// <summary>
        /// Gets or sets the scale pulse frequency (higher = faster).
        /// Typical range: 1.0 - 10.0
        /// </summary>
        [JsonPropertyName("scaleFrequency")]
        public float ScaleFrequency { get; set; }

        /// <summary>
        /// Gets or sets the minimum scale factor (1.0 = normal size).
        /// Typical range: 0.5 - 1.0
        /// </summary>
        [JsonPropertyName("scaleMin")]
        public float ScaleMin { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets the maximum scale factor (1.0 = normal size).
        /// Typical range: 1.0 - 2.0
        /// </summary>
        [JsonPropertyName("scaleMax")]
        public float ScaleMax { get; set; } = 1.0f;

        // ============================================
        // ColorCycle Effect Parameters
        // ============================================

        /// <summary>
        /// Gets or sets the ID of the color palette to use.
        /// Required when ColorCycle effect type is enabled.
        /// </summary>
        [JsonPropertyName("colorPaletteId")]
        public string? ColorPaletteId { get; set; }

        /// <summary>
        /// Gets or sets the color cycle speed (higher = faster rotation).
        /// Typical range: 0.1 - 10.0
        /// </summary>
        [JsonPropertyName("colorCycleSpeed")]
        public float ColorCycleSpeed { get; set; }

        /// <summary>
        /// Gets or sets how ColorCycle interacts with manual {COLOR} tags.
        /// </summary>
        [JsonPropertyName("colorMode")]
        public string ColorModeName { get; set; } = "Override";

        /// <summary>
        /// Gets the parsed color effect mode.
        /// </summary>
        [JsonIgnore]
        public ColorEffectMode ColorMode
        {
            get
            {
                if (
                    System.Enum.TryParse<ColorEffectMode>(
                        ColorModeName,
                        ignoreCase: true,
                        out var parsed
                    )
                )
                {
                    return parsed;
                }
                return ColorEffectMode.Override;
            }
        }

        /// <summary>
        /// Gets or sets how shadow is determined when ColorCycle is active.
        /// </summary>
        [JsonPropertyName("shadowMode")]
        public string ShadowModeName { get; set; } = "Derive";

        /// <summary>
        /// Gets the parsed shadow effect mode.
        /// </summary>
        [JsonIgnore]
        public ShadowEffectMode ShadowMode
        {
            get
            {
                if (
                    System.Enum.TryParse<ShadowEffectMode>(
                        ShadowModeName,
                        ignoreCase: true,
                        out var parsed
                    )
                )
                {
                    return parsed;
                }
                return ShadowEffectMode.Derive;
            }
        }

        /// <summary>
        /// Gets or sets the multiplier for deriving shadow from text color (0.0-1.0).
        /// Only used when shadowMode is "derive".
        /// </summary>
        [JsonPropertyName("shadowDeriveMultiplier")]
        public float ShadowDeriveMultiplier { get; set; } = 0.33f;

        // ============================================
        // Fade Effect Parameters
        // ============================================

        /// <summary>
        /// Gets or sets the fade oscillation frequency (higher = faster).
        /// Typical range: 0.5 - 10.0
        /// </summary>
        [JsonPropertyName("fadeFrequency")]
        public float FadeFrequency { get; set; }

        /// <summary>
        /// Gets or sets the minimum opacity (0.0 = invisible, 1.0 = fully visible).
        /// Typical range: 0.0 - 1.0
        /// </summary>
        [JsonPropertyName("fadeMin")]
        public float FadeMin { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets the maximum opacity (0.0 = invisible, 1.0 = fully visible).
        /// Typical range: 0.0 - 1.0
        /// </summary>
        [JsonPropertyName("fadeMax")]
        public float FadeMax { get; set; } = 1.0f;

        // ============================================
        // Glow Effect Parameters
        // ============================================

        /// <summary>
        /// Gets or sets the glow color. If null, derives from text color.
        /// </summary>
        [JsonPropertyName("glowColor")]
        public PaletteColor? GlowColor { get; set; }

        /// <summary>
        /// Gets or sets the glow radius in pixels.
        /// Typical range: 1 - 5
        /// </summary>
        [JsonPropertyName("glowRadius")]
        public int GlowRadius { get; set; } = 1;

        /// <summary>
        /// Gets or sets the glow opacity (0.0 - 1.0).
        /// </summary>
        [JsonPropertyName("glowOpacity")]
        public float GlowOpacity { get; set; } = 0.5f;

        /// <summary>
        /// Gets or sets whether glow pulses with the text (uses fade parameters).
        /// </summary>
        [JsonPropertyName("glowPulses")]
        public bool GlowPulses { get; set; }

        // ============================================
        // Per-Effect Phase Offsets
        // ============================================

        /// <summary>
        /// Gets or sets the phase offset for wobble effect per character.
        /// If null, uses wavePhaseOffset.
        /// </summary>
        [JsonPropertyName("wobblePhaseOffset")]
        public float? WobblePhaseOffset { get; set; }

        /// <summary>
        /// Gets or sets the phase offset for scale effect per character.
        /// If null, uses wavePhaseOffset.
        /// </summary>
        [JsonPropertyName("scalePhaseOffset")]
        public float? ScalePhaseOffset { get; set; }

        /// <summary>
        /// Gets or sets the phase offset for fade effect per character.
        /// If null, uses wavePhaseOffset.
        /// </summary>
        [JsonPropertyName("fadePhaseOffset")]
        public float? FadePhaseOffset { get; set; }

        /// <summary>
        /// Gets or sets the phase offset for color cycling per character.
        /// If null, uses wavePhaseOffset.
        /// </summary>
        [JsonPropertyName("colorPhaseOffset")]
        public float? ColorPhaseOffset { get; set; }

        /// <summary>
        /// Gets the effective phase offset for wobble, falling back to wavePhaseOffset.
        /// </summary>
        [JsonIgnore]
        public float EffectiveWobblePhaseOffset => WobblePhaseOffset ?? WavePhaseOffset;

        /// <summary>
        /// Gets the effective phase offset for scale, falling back to wavePhaseOffset.
        /// </summary>
        [JsonIgnore]
        public float EffectiveScalePhaseOffset => ScalePhaseOffset ?? WavePhaseOffset;

        /// <summary>
        /// Gets the effective phase offset for fade, falling back to wavePhaseOffset.
        /// </summary>
        [JsonIgnore]
        public float EffectiveFadePhaseOffset => FadePhaseOffset ?? WavePhaseOffset;

        /// <summary>
        /// Gets the effective phase offset for color, falling back to wavePhaseOffset.
        /// </summary>
        [JsonIgnore]
        public float EffectiveColorPhaseOffset => ColorPhaseOffset ?? WavePhaseOffset;

        // ============================================
        // Typewriter Speed Override
        // ============================================

        /// <summary>
        /// Gets or sets the typewriter speed multiplier while this effect is active.
        /// 1.0 = normal speed, 0.5 = half speed (slower), 2.0 = double speed.
        /// Null means no override.
        /// </summary>
        [JsonPropertyName("typewriterSpeedMultiplier")]
        public float? TypewriterSpeedMultiplier { get; set; }

        // ============================================
        // Shake Seed Control
        // ============================================

        /// <summary>
        /// Gets or sets whether shake uses a deterministic seed for consistent patterns.
        /// </summary>
        [JsonPropertyName("deterministicShake")]
        public bool DeterministicShake { get; set; }

        /// <summary>
        /// Gets or sets the random seed for deterministic shake.
        /// Only used when deterministicShake is true.
        /// </summary>
        [JsonPropertyName("shakeRandomSeed")]
        public int ShakeRandomSeed { get; set; } = 12345;

        // ============================================
        // Wobble Origin
        // ============================================

        /// <summary>
        /// Gets or sets the rotation pivot point for wobble effect.
        /// </summary>
        [JsonPropertyName("wobbleOrigin")]
        public string WobbleOriginName { get; set; } = "Center";

        /// <summary>
        /// Gets the parsed wobble origin.
        /// </summary>
        [JsonIgnore]
        public WobbleOrigin WobbleOrigin
        {
            get
            {
                if (
                    System.Enum.TryParse<WobbleOrigin>(
                        WobbleOriginName,
                        ignoreCase: true,
                        out var parsed
                    )
                )
                {
                    return parsed;
                }
                return WobbleOrigin.Center;
            }
        }

        // ============================================
        // Spacing Adjustments
        // ============================================

        /// <summary>
        /// Gets or sets additional letter spacing in pixels.
        /// Positive values spread characters apart, negative brings them closer.
        /// </summary>
        [JsonPropertyName("letterSpacingOffset")]
        public float LetterSpacingOffset { get; set; }

        /// <summary>
        /// Gets or sets vertical offset in pixels (positive = down, negative = up).
        /// Useful for superscript/subscript effects.
        /// </summary>
        [JsonPropertyName("verticalOffset")]
        public float VerticalOffset { get; set; }

        // ============================================
        // Sound Triggers
        // ============================================

        /// <summary>
        /// Gets or sets the sound effect ID to play when this effect starts.
        /// </summary>
        [JsonPropertyName("onStartSound")]
        public string? OnStartSound { get; set; }

        /// <summary>
        /// Gets or sets the sound effect ID to play when this effect ends.
        /// </summary>
        [JsonPropertyName("onEndSound")]
        public string? OnEndSound { get; set; }

        /// <summary>
        /// Gets or sets the sound effect ID to play per character while this effect is active.
        /// </summary>
        [JsonPropertyName("perCharacterSound")]
        public string? PerCharacterSound { get; set; }

        /// <summary>
        /// Gets or sets the minimum interval between per-character sounds in seconds.
        /// Prevents sound spam for fast text.
        /// </summary>
        [JsonPropertyName("perCharacterSoundInterval")]
        public float PerCharacterSoundInterval { get; set; } = 0.05f;

        /// <summary>
        /// Gets or sets the version of the effect definition.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }
}
