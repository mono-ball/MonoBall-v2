namespace MonoBall.Core.Scenes.Events;

/// <summary>
///     Event fired when a scene's priority changes.
/// </summary>
public struct ScenePriorityChangedEvent
{
    /// <summary>
    ///     The scene ID.
    /// </summary>
    public string SceneId { get; set; }

    /// <summary>
    ///     The old priority value.
    /// </summary>
    public int OldPriority { get; set; }

    /// <summary>
    ///     The new priority value.
    /// </summary>
    public int NewPriority { get; set; }
}
