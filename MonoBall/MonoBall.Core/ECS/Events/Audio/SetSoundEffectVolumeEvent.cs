namespace MonoBall.Core.ECS.Events.Audio
{
    /// <summary>
    /// Event fired to change sound effect volume.
    /// </summary>
    public struct SetSoundEffectVolumeEvent
    {
        /// <summary>
        /// Sound effect volume (0-1).
        /// </summary>
        public float Volume { get; set; }
    }
}
