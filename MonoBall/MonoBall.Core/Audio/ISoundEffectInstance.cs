namespace MonoBall.Core.Audio;

/// <summary>
///     Interface for controlling a sound effect instance.
/// </summary>
public interface ISoundEffectInstance
{
    /// <summary>
    ///     Gets whether the sound effect is currently playing.
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    ///     Gets or sets the volume (0.0 - 1.0).
    /// </summary>
    float Volume { get; set; }

    /// <summary>
    ///     Gets or sets the pitch adjustment (-1.0 to 1.0).
    /// </summary>
    float Pitch { get; set; }

    /// <summary>
    ///     Gets or sets the pan adjustment (-1.0 left to 1.0 right).
    /// </summary>
    float Pan { get; set; }

    /// <summary>
    ///     Stops the sound effect.
    /// </summary>
    void Stop();

    /// <summary>
    ///     Pauses the sound effect.
    /// </summary>
    void Pause();

    /// <summary>
    ///     Resumes the sound effect.
    /// </summary>
    void Resume();
}
