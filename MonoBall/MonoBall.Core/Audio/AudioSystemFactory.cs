using System;
using System.Collections.Generic;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Systems.Audio;
using MonoBall.Core.Logging;
using MonoBall.Core.Scenes;

namespace MonoBall.Core.Audio;

/// <summary>
///     Factory for creating audio-related systems.
///     Extracts audio system creation logic from SystemManager.
/// </summary>
public sealed class AudioSystemFactory : IAudioSystemFactory
{
    /// <inheritdoc />
    public IAudioSystems Create(
        SystemCreationContext context,
        ISceneManager sceneManager,
        IAudioEngine audioEngine,
        IList<BaseSystem<Arch.Core.World, float>> updateSystems
    )
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (updateSystems == null)
            throw new ArgumentNullException(nameof(updateSystems));

        // If audio engine isn't available, return empty bundle
        if (audioEngine == null)
        {
            context.Logger.Warning("Audio engine not available, audio systems will be disabled");
            return new AudioSystems(null, null, null, null, null);
        }

        // Create map music system (needs scene manager for location-based music)
        var mapMusicSystem = new MapMusicSystem(
            context.World,
            sceneManager,
            LoggerFactory.CreateLogger<MapMusicSystem>()
        );
        updateSystems.Add(mapMusicSystem);

        // Create music playback system
        var musicPlaybackSystem = new MusicPlaybackSystem(
            context.World,
            context.ModManager.Registry,
            audioEngine,
            LoggerFactory.CreateLogger<MusicPlaybackSystem>()
        );
        updateSystems.Add(musicPlaybackSystem);

        // Create sound effect system
        var soundEffectSystem = new SoundEffectSystem(
            context.World,
            context.ModManager.Registry,
            audioEngine,
            LoggerFactory.CreateLogger<SoundEffectSystem>()
        );
        updateSystems.Add(soundEffectSystem);

        // Create ambient sound system
        var ambientSoundSystem = new AmbientSoundSystem(
            context.World,
            context.ModManager.Registry,
            audioEngine,
            LoggerFactory.CreateLogger<AmbientSoundSystem>()
        );
        updateSystems.Add(ambientSoundSystem);

        // Create audio volume system
        var audioVolumeSystem = new AudioVolumeSystem(
            context.World,
            audioEngine,
            LoggerFactory.CreateLogger<AudioVolumeSystem>()
        );
        updateSystems.Add(audioVolumeSystem);

        context.Logger.Debug("Audio systems created successfully");

        return new AudioSystems(
            mapMusicSystem,
            musicPlaybackSystem,
            soundEffectSystem,
            ambientSoundSystem,
            audioVolumeSystem
        );
    }
}
