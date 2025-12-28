namespace MonoBall.Core.TextEffects
{
    /// <summary>
    /// Determines how shadow color is handled when ColorCycle is active.
    /// </summary>
    public enum ShadowEffectMode
    {
        /// <summary>
        /// Derive shadow from the cycled text color (darker version).
        /// Shadow RGB = TextColor RGB * ShadowDeriveMultiplier.
        /// </summary>
        Derive,

        /// <summary>
        /// Preserve manual {SHADOW} or default shadow color.
        /// ColorCycle does not affect shadow.
        /// </summary>
        Preserve,
    }
}
