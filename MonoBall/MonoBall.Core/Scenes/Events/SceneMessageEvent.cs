namespace MonoBall.Core.Scenes.Events
{
    /// <summary>
    /// Generic message event for inter-scene communication.
    /// </summary>
    public struct SceneMessageEvent
    {
        /// <summary>
        /// The scene ID sending the message.
        /// </summary>
        public string SourceSceneId { get; set; }

        /// <summary>
        /// The target scene ID (null = broadcast to all scenes).
        /// </summary>
        public string? TargetSceneId { get; set; }

        /// <summary>
        /// Type of message (e.g., "pause", "resume", "destroy", "custom").
        /// </summary>
        public string MessageType { get; set; }

        /// <summary>
        /// Optional message payload.
        /// </summary>
        public object? MessageData { get; set; }
    }
}
