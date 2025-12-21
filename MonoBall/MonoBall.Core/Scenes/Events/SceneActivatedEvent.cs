namespace MonoBall.Core.Scenes.Events
{
    /// <summary>
    /// Event fired when a scene becomes active.
    /// </summary>
    public struct SceneActivatedEvent
    {
        /// <summary>
        /// The scene ID.
        /// </summary>
        public string SceneId { get; set; }
    }
}
