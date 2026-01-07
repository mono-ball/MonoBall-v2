namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when player presses A/B button to advance text.
/// </summary>
public struct MessageBoxTextAdvanceEvent
{
    /// <summary>
    ///     Window ID of the message box.
    /// </summary>
    public int WindowId { get; set; }
}
