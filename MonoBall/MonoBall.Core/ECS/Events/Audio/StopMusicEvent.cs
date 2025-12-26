namespace MonoBall.Core.ECS.Events.Audio
{
    /// <summary>
    /// Event fired to request stopping background music.
    /// </summary>
    public struct StopMusicEvent
    {
        /// <summary>
        /// Fade-out duration in seconds (0 = instant).
        /// </summary>
        public float FadeOutDuration { get; set; }
    }
}
