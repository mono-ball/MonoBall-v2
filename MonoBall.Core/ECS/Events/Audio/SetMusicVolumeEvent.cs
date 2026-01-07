namespace MonoBall.Core.ECS.Events.Audio;

/// <summary>
///     Event fired to change music volume.
/// </summary>
public struct SetMusicVolumeEvent
{
    /// <summary>
    ///     Music volume (0-1).
    /// </summary>
    public float Volume { get; set; }
}
