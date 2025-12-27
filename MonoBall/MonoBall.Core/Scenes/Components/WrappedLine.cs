namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Represents a wrapped line of text.
    /// Used for efficient text rendering (pre-wrapped, not wrapped every frame).
    /// </summary>
    public struct WrappedLine
    {
        /// <summary>
        /// The text substring for this line.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// The start character index in the original text.
        /// </summary>
        public int StartIndex { get; set; }

        /// <summary>
        /// The end character index in the original text (exclusive).
        /// </summary>
        public int EndIndex { get; set; }

        /// <summary>
        /// The pixel width of this line.
        /// </summary>
        public float Width { get; set; }
    }
}
