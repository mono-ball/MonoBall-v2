namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that flags entities for rendering and controls render order.
/// </summary>
public struct RenderableComponent
{
    /// <summary>
    ///     Whether this entity should be rendered.
    /// </summary>
    public bool IsVisible { get; set; }

    /// <summary>
    ///     The render order (lower values render first).
    /// </summary>
    public int RenderOrder { get; set; }

    /// <summary>
    ///     The opacity of the entity (0.0 to 1.0).
    /// </summary>
    public float Opacity { get; set; }
}
