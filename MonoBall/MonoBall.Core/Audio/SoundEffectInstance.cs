using System;

namespace MonoBall.Core.Audio;

/// <summary>
///     Implementation of ISoundEffectInstance for controlling sound effect playback.
/// </summary>
public class SoundEffectInstance : ISoundEffectInstance
{
    private bool _isPaused;
    private bool _isPlaying;
    private float _pan;
    private float _pitch;
    private float _volume;

    /// <summary>
    ///     Initializes a new instance of the SoundEffectInstance.
    /// </summary>
    /// <param name="volume">Initial volume (0.0 - 1.0).</param>
    /// <param name="pitch">Initial pitch adjustment (-1.0 to 1.0).</param>
    /// <param name="pan">Initial pan adjustment (-1.0 left to 1.0 right).</param>
    public SoundEffectInstance(float volume = 1.0f, float pitch = 0.0f, float pan = 0.0f)
    {
        _volume = volume;
        _pitch = pitch;
        _pan = pan;
        _isPlaying = false;
        _isPaused = false;
    }

    /// <summary>
    ///     Gets whether the sound effect is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying && !_isPaused;
        set => _isPlaying = value;
    }

    /// <summary>
    ///     Gets or sets the volume (0.0 - 1.0).
    ///     Note: Setting this property only updates internal state. To actually change playback volume,
    ///     use IAudioEngine.SetSoundEffectVolume() which applies the change to active playback.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0f, 1.0f);
    }

    /// <summary>
    ///     Gets or sets the pitch adjustment (-1.0 to 1.0).
    ///     Note: Setting this property only updates internal state. To actually change playback pitch,
    ///     use IAudioEngine.SetSoundEffectPitch() (pitch adjustment not yet implemented).
    /// </summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, -1.0f, 1.0f);
    }

    /// <summary>
    ///     Gets or sets the pan adjustment (-1.0 left to 1.0 right).
    ///     Note: Setting this property only updates internal state. To actually change playback pan,
    ///     use IAudioEngine.SetSoundEffectPan() (pan adjustment not yet implemented).
    /// </summary>
    public float Pan
    {
        get => _pan;
        set => _pan = Math.Clamp(value, -1.0f, 1.0f);
    }

    /// <summary>
    ///     Stops the sound effect.
    /// </summary>
    public void Stop()
    {
        _isPlaying = false;
        _isPaused = false;
    }

    /// <summary>
    ///     Pauses the sound effect.
    ///     Note: This method only updates internal state. Use IAudioEngine.PauseSound() to actually pause playback.
    /// </summary>
    public void Pause()
    {
        if (_isPlaying)
            _isPaused = true;
    }

    /// <summary>
    ///     Resumes the sound effect.
    ///     Note: This method only updates internal state. Use IAudioEngine.ResumeSound() to actually resume playback.
    /// </summary>
    public void Resume()
    {
        if (_isPaused)
            _isPaused = false;
    }
}
