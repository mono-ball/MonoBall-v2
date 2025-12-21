using Arch.Core;

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
    }
}
