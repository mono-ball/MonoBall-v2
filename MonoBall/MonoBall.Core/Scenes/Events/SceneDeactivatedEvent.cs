using Arch.Core;

namespace MonoBall.Core.Scenes.Events;

/// <summary>
///     Event fired when a scene becomes inactive.
/// </summary>
public struct SceneDeactivatedEvent
{
    /// <summary>
    ///     The scene ID.
    /// </summary>
    public string SceneId { get; set; }

    /// <summary>
    ///     The scene entity.
    /// </summary>
    public Entity SceneEntity { get; set; }

    /// <summary>
    ///     The scene priority.
    /// </summary>
    public int Priority { get; set; }
}
