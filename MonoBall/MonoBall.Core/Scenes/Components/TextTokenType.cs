namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Types of text tokens.
    /// </summary>
    public enum TextTokenType
    {
        /// <summary>
        /// Regular character.
        /// </summary>
        Char,

        /// <summary>
        /// Newline character.
        /// </summary>
        Newline,

        /// <summary>
        /// Pause control code.
        /// </summary>
        Pause,

        /// <summary>
        /// Pause until button press.
        /// </summary>
        PauseUntilPress,

        /// <summary>
        /// Color change control code (foreground text color).
        /// </summary>
        Color,

        /// <summary>
        /// Shadow color change control code.
        /// </summary>
        Shadow,

        /// <summary>
        /// Speed change control code.
        /// </summary>
        Speed,

        /// <summary>
        /// Clear page control code.
        /// </summary>
        Clear,

        /// <summary>
        /// Page break - newline + wait for input (like Pokemon's \p).
        /// Use for single-line pages or forced page breaks.
        /// Clears the box and starts fresh.
        /// </summary>
        PageBreak,

        /// <summary>
        /// Scroll - newline + wait for input + scroll up (like Pokemon's \l).
        /// Keeps previous line visible while adding new line at bottom.
        /// Use for flowing 3+ line messages.
        /// </summary>
        Scroll,

        /// <summary>
        /// Reset control code - restores color, shadow, and speed to their initial defaults.
        /// </summary>
        Reset,

        /// <summary>
        /// Start text effect: {FX:effectId}
        /// Applies animated effects to subsequent characters.
        /// </summary>
        EffectStart,

        /// <summary>
        /// End text effect: {/FX}
        /// Stops applying effects to subsequent characters.
        /// </summary>
        EffectEnd,
    }
}
