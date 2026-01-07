namespace MonoBall.Core.Scenes.Events;

/// <summary>
///     Event fired when a scene is about to be created (before entity creation).
///     Allows systems to prepare or cancel scene creation.
/// </summary>
public struct SceneCreatingEvent
{
    /// <summary>
    ///     The scene ID that will be created.
    /// </summary>
    public string SceneId { get; set; }

    /// <summary>
    ///     Whether to cancel scene creation. Set to true to prevent scene from being created.
    /// </summary>
    public bool Cancel { get; set; }
}
