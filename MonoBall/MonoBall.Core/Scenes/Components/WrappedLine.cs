using System.Collections.Generic;

namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Represents a wrapped line of text with optional per-character render data.
///     Used for efficient text rendering (pre-wrapped, not wrapped every frame).
/// </summary>
public struct WrappedLine
{
    /// <summary>
    ///     The text substring for this line.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    ///     The start character index in the original text.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    ///     The end character index in the original text (exclusive).
    /// </summary>
    public int EndIndex { get; set; }

    /// <summary>
    ///     The pixel width of this line.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    ///     Pre-calculated per-character render data.
    ///     Null if line has no effects (fast path rendering).
    /// </summary>
    public List<CharacterRenderData>? CharacterData { get; set; }

    /// <summary>
    ///     Whether this line contains any text effects.
    ///     Used for fast-path check to skip per-character rendering.
    /// </summary>
    public bool HasEffects { get; set; }
}
