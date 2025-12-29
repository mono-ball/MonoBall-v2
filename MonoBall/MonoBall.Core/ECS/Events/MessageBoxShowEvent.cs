using Microsoft.Xna.Framework;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a message box should be shown.
/// </summary>
public struct MessageBoxShowEvent
{
    /// <summary>
    ///     The text to display.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    ///     Text speed override in seconds per character (null = use player preference).
    /// </summary>
    public float? TextSpeedOverride { get; set; }

    /// <summary>
    ///     Whether A/B button can speed up printing.
    /// </summary>
    public bool CanSpeedUpWithButton { get; set; }

    /// <summary>
    ///     Whether text should auto-scroll.
    /// </summary>
    public bool AutoScroll { get; set; }

    /// <summary>
    ///     Font ID to use (null = default).
    /// </summary>
    public string? FontId { get; set; }

    /// <summary>
    ///     Text color (foreground). Null = use default (Dark Gray: 98, 98, 98).
    /// </summary>
    public Color? TextColor { get; set; }

    /// <summary>
    ///     Background color. Null = use default (Transparent).
    /// </summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>
    ///     Shadow color for text. Null = use default (Dark Gray).
    /// </summary>
    public Color? ShadowColor { get; set; }
}
