using Arch.Core;

namespace MonoBall.Core.Scenes.Events
{
    /// <summary>
    /// Event fired when a scene resumes.
    /// </summary>
    public struct SceneResumedEvent
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
