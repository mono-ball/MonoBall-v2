using System;
using Arch.Core;

namespace MonoBall.Core.Scenes.Events;

/// <summary>
///     Event fired when a scene is destroyed.
/// </summary>
public struct SceneDestroyedEvent
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
    ///     The scene priority at time of destruction.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     Optional scene type identifier (e.g., "GameScene", "LoadingScene").
    /// </summary>
    public string? SceneType { get; set; }

    /// <summary>
    ///     Timestamp when the scene was destroyed.
    /// </summary>
    public DateTime DestroyedAt { get; set; }
}
