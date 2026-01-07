namespace MonoBall.Core.ECS.Events.Audio;

/// <summary>
///     Event fired to request playing background music.
/// </summary>
public struct PlayMusicEvent
{
    /// <summary>
    ///     The audio definition ID for the music track.
    /// </summary>
    public string AudioId { get; set; }

    /// <summary>
    ///     Whether the music should loop.
    /// </summary>
    public bool Loop { get; set; }

    /// <summary>
    ///     Fade-in duration in seconds (0 = instant).
    /// </summary>
    public float FadeInDuration { get; set; }

    /// <summary>
    ///     Whether to crossfade with current music (if playing).
    /// </summary>
    public bool Crossfade { get; set; }

    /// <summary>
    ///     Crossfade duration in seconds (only used if Crossfade is true).
    /// </summary>
    public float CrossfadeDuration { get; set; }
}
