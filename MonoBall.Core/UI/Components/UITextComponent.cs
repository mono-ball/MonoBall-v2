using Microsoft.Xna.Framework;

namespace MonoBall.Core.UI.Components;

/// <summary>
///     Component that stores text rendering data for UI elements.
///     Used for text content in windows, labels, buttons, etc.
/// </summary>
public struct UITextComponent
{
    /// <summary>
    ///     The text content to render (current visible characters).
    ///     Updated by systems like MessageBoxSceneSystem as text is printed character-by-character.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    ///     The font ID to use for rendering.
    /// </summary>
    public string FontId { get; set; }

    /// <summary>
    ///     The font size in pixels.
    /// </summary>
    public int FontSize { get; set; }

    /// <summary>
    ///     The text color.
    /// </summary>
    public Color TextColor { get; set; }

    /// <summary>
    ///     The shadow color (if text has shadow).
    /// </summary>
    public Color? ShadowColor { get; set; }

    /// <summary>
    ///     Text alignment (Left, Center, Right).
    /// </summary>
    public TextAlignment Alignment { get; set; }

    /// <summary>
    ///     Line spacing in pixels.
    /// </summary>
    public int LineSpacing { get; set; }
}

/// <summary>
///     Text alignment options for UI text.
/// </summary>
public enum TextAlignment
{
    Left,
    Center,
    Right
}
