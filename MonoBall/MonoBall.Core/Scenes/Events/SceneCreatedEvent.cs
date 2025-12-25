using System;
using Arch.Core;
using MonoBall.Core.Scenes;

namespace MonoBall.Core.Scenes.Events
{
    /// <summary>
    /// Event fired when a scene is created.
    /// </summary>
    public struct SceneCreatedEvent
    {
        /// <summary>
        /// The scene ID.
        /// </summary>
        public string SceneId { get; set; }

        /// <summary>
        /// The scene entity.
        /// </summary>
        public Entity SceneEntity { get; set; }

        /// <summary>
        /// The scene priority.
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// The camera mode for the scene.
        /// </summary>
        public SceneCameraMode CameraMode { get; set; }

        /// <summary>
        /// Optional scene type identifier (e.g., "GameScene", "LoadingScene").
        /// </summary>
        public string? SceneType { get; set; }

        /// <summary>
        /// Timestamp when the scene was created.
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}
