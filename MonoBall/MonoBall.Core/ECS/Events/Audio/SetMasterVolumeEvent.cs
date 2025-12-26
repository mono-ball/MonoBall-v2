namespace MonoBall.Core.ECS.Events.Audio
{
    /// <summary>
    /// Event fired to change master volume.
    /// </summary>
    public struct SetMasterVolumeEvent
    {
        /// <summary>
        /// Master volume (0-1).
        /// </summary>
        public float Volume { get; set; }
    }
}
