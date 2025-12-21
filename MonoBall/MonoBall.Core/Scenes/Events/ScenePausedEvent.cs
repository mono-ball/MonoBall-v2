namespace MonoBall.Core.Scenes.Events
{
    /// <summary>
    /// Event fired when a scene is paused.
    /// </summary>
    public struct ScenePausedEvent
    {
        /// <summary>
        /// The scene ID.
        /// </summary>
        public string SceneId { get; set; }
    }
}
