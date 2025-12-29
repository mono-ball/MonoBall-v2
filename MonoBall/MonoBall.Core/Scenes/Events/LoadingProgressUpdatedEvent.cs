namespace MonoBall.Core.Scenes.Events;

/// <summary>
///     Event fired when loading progress is updated.
///     Allows systems to react to loading progress changes.
/// </summary>
public struct LoadingProgressUpdatedEvent
{
    /// <summary>
    ///     Current loading progress (0.0 to 1.0).
    /// </summary>
    public float Progress { get; set; }

    /// <summary>
    ///     Description of the current loading step.
    /// </summary>
    public string CurrentStep { get; set; }
}
