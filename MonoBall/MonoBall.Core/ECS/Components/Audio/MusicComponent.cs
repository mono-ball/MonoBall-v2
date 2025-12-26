namespace MonoBall.Core.ECS.Components.Audio
{
    /// <summary>
    /// Component attached to map entities to specify background music.
    /// </summary>
    public struct MusicComponent
    {
        /// <summary>
        /// The audio definition ID for the music track.
        /// </summary>
        public string AudioId { get; set; }

        /// <summary>
        /// Whether to fade in when transitioning to this map.
        /// </summary>
        public bool FadeInOnTransition { get; set; }

        /// <summary>
        /// Custom fade duration (0 = use definition default).
        /// </summary>
        public float FadeDuration { get; set; }
    }
}
