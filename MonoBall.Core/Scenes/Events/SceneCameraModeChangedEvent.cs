namespace MonoBall.Core.Scenes.Events;

/// <summary>
///     Event fired when a scene's camera mode changes.
/// </summary>
public struct SceneCameraModeChangedEvent
{
    /// <summary>
    ///     The scene ID.
    /// </summary>
    public string SceneId { get; set; }

    /// <summary>
    ///     The old camera mode.
    /// </summary>
    public SceneCameraMode OldMode { get; set; }

    /// <summary>
    ///     The new camera mode.
    /// </summary>
    public SceneCameraMode NewMode { get; set; }
}
