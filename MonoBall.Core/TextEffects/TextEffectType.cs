using System;

namespace MonoBall.Core.TextEffects;

/// <summary>
///     Types of text animation effects.
///     Flags enum to allow combining multiple effects.
/// </summary>
[Flags]
public enum TextEffectType
{
    /// <summary>
    ///     No effect applied.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Vertical sine wave motion.
    ///     Characters oscillate up and down in a wave pattern.
    /// </summary>
    Wave = 1 << 0,

    /// <summary>
    ///     Random position jitter.
    ///     Characters shake randomly within a defined strength.
    /// </summary>
    Shake = 1 << 1,

    /// <summary>
    ///     Vertical bounce effect (absolute sine).
    ///     Characters bounce up from baseline.
    /// </summary>
    Hang = 1 << 2,

    /// <summary>
    ///     Horizontal oscillation.
    ///     Characters move left and right.
    /// </summary>
    SideStep = 1 << 3,

    /// <summary>
    ///     Rotating color palette.
    ///     Characters cycle through colors from a palette.
    /// </summary>
    ColorCycle = 1 << 4,

    /// <summary>
    ///     Character rotation wobble.
    ///     Characters rotate back and forth like they're hanging and swinging.
    ///     Similar to Cool-Custom-Text's "Hang" effect.
    /// </summary>
    Wobble = 1 << 5,

    /// <summary>
    ///     Pulsing scale effect.
    ///     Characters grow and shrink rhythmically.
    /// </summary>
    Scale = 1 << 6,

    /// <summary>
    ///     Opacity fade effect.
    ///     Characters fade in and out rhythmically.
    /// </summary>
    Fade = 1 << 7,

    /// <summary>
    ///     Glow/outline effect.
    ///     Characters have a colored glow or outline.
    /// </summary>
    Glow = 1 << 8,
}
