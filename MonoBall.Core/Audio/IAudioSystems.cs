using System;

namespace MonoBall.Core.Audio;

/// <summary>
///     Bundle interface for audio-related systems.
///     Exposes interfaces, not concrete types, for proper dependency inversion.
/// </summary>
public interface IAudioSystems : IDisposable
{
    /// <summary>
    ///     Gets the map music system for location-based music.
    /// </summary>
    IMapMusicSystem? MapMusicSystem { get; }

    /// <summary>
    ///     Gets the music playback system.
    /// </summary>
    IMusicPlaybackSystem? MusicPlaybackSystem { get; }

    /// <summary>
    ///     Gets the sound effect system.
    /// </summary>
    ISoundEffectSystem? SoundEffectSystem { get; }

    /// <summary>
    ///     Gets the ambient sound system.
    /// </summary>
    IAmbientSoundSystem? AmbientSoundSystem { get; }

    /// <summary>
    ///     Gets the audio volume system.
    /// </summary>
    IAudioVolumeSystem? AudioVolumeSystem { get; }

    /// <summary>
    ///     Gets whether audio systems are available.
    /// </summary>
    bool IsAvailable { get; }
}
