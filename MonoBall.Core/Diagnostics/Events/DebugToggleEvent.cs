namespace MonoBall.Core.Diagnostics.Events;

/// <summary>
/// Event fired to toggle debug overlay visibility.
/// </summary>
public struct DebugToggleEvent
{
    /// <summary>
    /// If true, show the debug overlay. If false, hide it.
    /// If null, toggle current state.
    /// </summary>
    public bool? Show { get; set; }
}
