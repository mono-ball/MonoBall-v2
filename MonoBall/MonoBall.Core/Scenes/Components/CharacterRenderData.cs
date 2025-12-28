using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Pre-calculated render data for a single character.
    /// Computed once during text parsing, not per-frame.
    /// </summary>
    public struct CharacterRenderData
    {
        /// <summary>
        /// The character to render.
        /// </summary>
        public char Character { get; set; }

        /// <summary>
        /// Base X position relative to line start (pre-measured in unscaled pixels).
        /// </summary>
        public float BaseX { get; set; }

        /// <summary>
        /// Index of this character in the line (for phase offset calculations).
        /// </summary>
        public int CharIndex { get; set; }

        /// <summary>
        /// Text color at this character position.
        /// Set from {COLOR} tag or default.
        /// </summary>
        public Color TextColor { get; set; }

        /// <summary>
        /// Shadow color at this character position.
        /// Set from {SHADOW} tag or default.
        /// </summary>
        public Color ShadowColor { get; set; }

        /// <summary>
        /// Effect definition ID to apply.
        /// Empty string means no effect.
        /// </summary>
        public string EffectId { get; set; }

        /// <summary>
        /// Whether TextColor was explicitly set via {COLOR} tag.
        /// Used for "preserve" colorMode.
        /// </summary>
        public bool HasManualColor { get; set; }

        /// <summary>
        /// Whether ShadowColor was explicitly set via {SHADOW} tag.
        /// Used for "preserve" shadowMode.
        /// </summary>
        public bool HasManualShadow { get; set; }
    }
}
