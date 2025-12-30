namespace MonoBall.Core.Diagnostics.Events;

/// <summary>
/// Event fired to toggle a specific debug panel's visibility.
/// </summary>
public struct DebugPanelToggleEvent
{
    /// <summary>
    /// The unique identifier of the panel to toggle.
    /// </summary>
    public string PanelId { get; set; }

    /// <summary>
    /// If true, show the panel. If false, hide it.
    /// If null, toggle current state.
    /// </summary>
    public bool? Show { get; set; }
}
