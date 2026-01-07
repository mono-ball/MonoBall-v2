namespace MonoBall.Core.Audio;

/// <summary>
///     Interface for low-level audio playback abstraction.
/// </summary>
public interface IAudioEngine
{
    /// <summary>
    ///     Gets or sets the master volume (0.0 - 1.0).
    /// </summary>
    float MasterVolume { get; set; }

    /// <summary>
    ///     Gets or sets the music volume (0.0 - 1.0).
    /// </summary>
    float MusicVolume { get; set; }

    /// <summary>
    ///     Gets or sets the sound effect volume (0.0 - 1.0).
    /// </summary>
    float SoundEffectVolume { get; set; }

    /// <summary>
    ///     Plays a sound effect.
    ///     Throws exceptions on failure (fail fast per .cursorrules).
    /// </summary>
    /// <param name="audioId">The audio definition ID.</param>
    /// <param name="volume">Volume (0.0 - 1.0).</param>
    /// <param name="pitch">Pitch adjustment (-1.0 to 1.0).</param>
    /// <param name="pan">Pan adjustment (-1.0 left to 1.0 right).</param>
    /// <returns>The sound effect instance.</returns>
    /// <exception cref="ArgumentException">Thrown when audioId is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when audio definition not found or audio loading fails.</exception>
    /// <exception cref="FileNotFoundException">Thrown when audio file not found.</exception>
    ISoundEffectInstance PlaySound(string audioId, float volume, float pitch, float pan);

    /// <summary>
    ///     Plays a looping sound effect.
    ///     Throws exceptions on failure (fail fast per .cursorrules).
    /// </summary>
    /// <param name="audioId">The audio definition ID.</param>
    /// <param name="volume">Volume (0.0 - 1.0).</param>
    /// <returns>The sound effect instance.</returns>
    /// <exception cref="ArgumentException">Thrown when audioId is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when audio definition not found or audio loading fails.</exception>
    /// <exception cref="FileNotFoundException">Thrown when audio file not found.</exception>
    ISoundEffectInstance PlayLoopingSound(string audioId, float volume);

    /// <summary>
    ///     Stops a sound effect instance.
    /// </summary>
    /// <param name="instance">The sound effect instance to stop.</param>
    void StopSound(ISoundEffectInstance instance);

    /// <summary>
    ///     Pauses a sound effect instance.
    /// </summary>
    /// <param name="instance">The sound effect instance to pause.</param>
    void PauseSound(ISoundEffectInstance instance);

    /// <summary>
    ///     Resumes a sound effect instance.
    /// </summary>
    /// <param name="instance">The sound effect instance to resume.</param>
    void ResumeSound(ISoundEffectInstance instance);

    /// <summary>
    ///     Updates the volume of a sound effect instance.
    /// </summary>
    /// <param name="instance">The sound effect instance.</param>
    /// <param name="volume">The new volume (0.0 - 1.0).</param>
    void SetSoundEffectVolume(ISoundEffectInstance instance, float volume);

    /// <summary>
    ///     Updates the pitch of a sound effect instance.
    ///     Note: Pitch adjustment is not yet implemented - this method is reserved for future use.
    /// </summary>
    /// <param name="instance">The sound effect instance.</param>
    /// <param name="pitch">The new pitch adjustment (-1.0 to 1.0).</param>
    void SetSoundEffectPitch(ISoundEffectInstance instance, float pitch);

    /// <summary>
    ///     Updates the pan of a sound effect instance.
    ///     Note: Pan adjustment is not yet implemented - this method is reserved for future use.
    /// </summary>
    /// <param name="instance">The sound effect instance.</param>
    /// <param name="pan">The new pan adjustment (-1.0 left to 1.0 right).</param>
    void SetSoundEffectPan(ISoundEffectInstance instance, float pan);

    /// <summary>
    ///     Plays background music.
    /// </summary>
    /// <param name="audioId">The audio definition ID.</param>
    /// <param name="loop">Whether the music should loop.</param>
    /// <param name="fadeInDuration">Fade-in duration in seconds (0 = instant).</param>
    void PlayMusic(string audioId, bool loop, float fadeInDuration);

    /// <summary>
    ///     Stops background music.
    /// </summary>
    /// <param name="fadeOutDuration">Fade-out duration in seconds (0 = instant).</param>
    void StopMusic(float fadeOutDuration);

    /// <summary>
    ///     Pauses background music.
    /// </summary>
    void PauseMusic();

    /// <summary>
    ///     Resumes background music.
    /// </summary>
    void ResumeMusic();

    /// <summary>
    ///     Crossfades from current music to new music.
    /// </summary>
    /// <param name="newAudioId">The audio definition ID of the new music track.</param>
    /// <param name="crossfadeDuration">Crossfade duration in seconds.</param>
    /// <param name="loop">Whether the new music should loop.</param>
    void CrossfadeMusic(string newAudioId, float crossfadeDuration, bool loop);

    /// <summary>
    ///     Updates the audio engine. Should be called every frame.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since last update in seconds.</param>
    void Update(float deltaTime);
}
