namespace MonoBall.Core.Scenes.Events
{
    /// <summary>
    /// Event fired when a scene becomes inactive.
    /// </summary>
    public struct SceneDeactivatedEvent
    {
        /// <summary>
        /// The scene ID.
        /// </summary>
        public string SceneId { get; set; }
    }
}
