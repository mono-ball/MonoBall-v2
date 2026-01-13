namespace MonoBall.Core.UI.Components;

/// <summary>
///     Component that stores window-specific data (border, background, content configuration).
///     Entities with this component represent window-like UI elements (message boxes, popups, panels).
///     Position is stored in PositionComponent, not here (avoids duplication).
/// </summary>
public struct WindowComponent
{
    /// <summary>
    ///     The border/outline definition ID (e.g., "base:textwindow:tilesheet/message_box").
    ///     If null, no border is rendered.
    /// </summary>
    public string? BorderId { get; set; }

    /// <summary>
    ///     The background definition ID (e.g., "base:popup:background/default").
    ///     If null, no background is rendered.
    /// </summary>
    public string? BackgroundId { get; set; }

    /// <summary>
    ///     The interior width in pixels (at 1x scale, before viewport scaling).
    /// </summary>
    public int InteriorWidth { get; set; }

    /// <summary>
    ///     The interior height in pixels (at 1x scale, before viewport scaling).
    /// </summary>
    public int InteriorHeight { get; set; }
}
