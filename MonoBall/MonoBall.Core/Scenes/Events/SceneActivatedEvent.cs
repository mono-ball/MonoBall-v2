using Arch.Core;

namespace MonoBall.Core.Scenes.Events;

/// <summary>
///     Event fired when a scene becomes active.
/// </summary>
public struct SceneActivatedEvent
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
