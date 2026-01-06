using System;
using MonoBall.Core.ECS.Systems.Audio;

namespace MonoBall.Core.Audio;

/// <summary>
///     Bundle implementation for audio-related systems.
///     Owns and disposes all audio systems.
/// </summary>
public sealed class AudioSystems : IAudioSystems
{
    // Concrete types for disposal
    private readonly MapMusicSystem? _mapMusicSystemConcrete;
    private readonly MusicPlaybackSystem? _musicPlaybackSystemConcrete;
    private readonly AmbientSoundSystem? _ambientSoundSystemConcrete;
    private readonly AudioVolumeSystem? _audioVolumeSystemConcrete;
    private bool _isDisposed;

    /// <summary>
    ///     Initializes a new instance of the AudioSystems bundle.
    /// </summary>
    public AudioSystems(
        MapMusicSystem? mapMusicSystem,
        MusicPlaybackSystem? musicPlaybackSystem,
        SoundEffectSystem? soundEffectSystem,
        AmbientSoundSystem? ambientSoundSystem,
        AudioVolumeSystem? audioVolumeSystem
    )
    {
        // Store concrete types for disposal
        _mapMusicSystemConcrete = mapMusicSystem;
        _musicPlaybackSystemConcrete = musicPlaybackSystem;
        _ambientSoundSystemConcrete = ambientSoundSystem;
        _audioVolumeSystemConcrete = audioVolumeSystem;

        // Expose as interfaces
        MapMusicSystem = mapMusicSystem;
        MusicPlaybackSystem = musicPlaybackSystem;
        SoundEffectSystem = soundEffectSystem;
        AmbientSoundSystem = ambientSoundSystem;
        AudioVolumeSystem = audioVolumeSystem;
        IsAvailable = mapMusicSystem != null;
    }

    /// <inheritdoc />
    public IMapMusicSystem? MapMusicSystem { get; }

    /// <inheritdoc />
    public IMusicPlaybackSystem? MusicPlaybackSystem { get; }

    /// <inheritdoc />
    public ISoundEffectSystem? SoundEffectSystem { get; }

    /// <inheritdoc />
    public IAmbientSoundSystem? AmbientSoundSystem { get; }

    /// <inheritdoc />
    public IAudioVolumeSystem? AudioVolumeSystem { get; }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        // Dispose systems in reverse creation order using concrete types
        _audioVolumeSystemConcrete?.Dispose();
        _ambientSoundSystemConcrete?.Dispose();
        // SoundEffectSystem doesn't need disposal (no event subscriptions)
        _musicPlaybackSystemConcrete?.Dispose();
        _mapMusicSystemConcrete?.Dispose();
    }
}
