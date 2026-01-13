namespace MonoBall.Core.UI.Components;

/// <summary>
///     Component that identifies an entity as a UI element and stores UI-specific metadata.
///     All UI entities (windows, sprites, text, etc.) should have this component.
/// </summary>
public struct UIElementComponent
{
    /// <summary>
    ///     The type of UI element (Window, Sprite, Text, Border, Background, etc.).
    /// </summary>
    public UIElementType ElementType { get; set; }

    /// <summary>
    ///     The z-order for rendering (higher values render on top).
    ///     Used within the same scene/relationship hierarchy.
    /// </summary>
    public int ZOrder { get; set; }

    /// <summary>
    ///     Whether this element can receive input events.
    /// </summary>
    public bool IsInteractive { get; set; }

    /// <summary>
    ///     Optional element ID for scripting API access.
    /// </summary>
    public string? ElementId { get; set; }
}

/// <summary>
///     Types of UI elements.
/// </summary>
public enum UIElementType
{
    Window,
    Sprite,
    Text,
    Border,
    Background,
    Button,
    Panel,
    Other
}
