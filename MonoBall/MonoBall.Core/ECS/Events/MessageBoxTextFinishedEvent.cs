namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when message box finishes printing all text.
/// </summary>
public struct MessageBoxTextFinishedEvent
{
    /// <summary>
    ///     Window ID of the message box.
    /// </summary>
    public int WindowId { get; set; }
}
