using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.Audio;
using MonoBall.Core.ECS.Events.Audio;
using Serilog;

namespace MonoBall.Core.ECS.Systems.Audio;

/// <summary>
///     System that manages audio volume settings and applies them to the audio engine.
/// </summary>
public class AudioVolumeSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the AudioVolumeSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="audioEngine">The audio engine to apply volume settings to.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public AudioVolumeSystem(World world, IAudioEngine audioEngine, ILogger logger)
        : base(world)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        EventBus.Subscribe<SetMasterVolumeEvent>(OnMasterVolumeChanged);
        EventBus.Subscribe<SetMusicVolumeEvent>(OnMusicVolumeChanged);
        EventBus.Subscribe<SetSoundEffectVolumeEvent>(OnSoundEffectVolumeChanged);
    }

    /// <summary>
    ///     Disposes the system and unsubscribes from events.
    /// </summary>
    /// <remarks>
    ///     Implements IDisposable to properly clean up event subscriptions.
    ///     Uses standard dispose pattern without finalizer since only managed resources are disposed.
    ///     Uses 'new' keyword because BaseSystem may have a Dispose() method with different signature.
    /// </remarks>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.Audio + 30;

    private void OnMasterVolumeChanged(ref SetMasterVolumeEvent evt)
    {
        try
        {
            var clampedVolume = Math.Clamp(evt.Volume, 0f, 1f);
            _audioEngine.MasterVolume = clampedVolume;
            _logger.Debug("Master volume set to {Volume}", clampedVolume);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling SetMasterVolumeEvent");
        }
    }

    private void OnMusicVolumeChanged(ref SetMusicVolumeEvent evt)
    {
        try
        {
            var clampedVolume = Math.Clamp(evt.Volume, 0f, 1f);
            _audioEngine.MusicVolume = clampedVolume;
            _logger.Debug("Music volume set to {Volume}", clampedVolume);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling SetMusicVolumeEvent");
        }
    }

    private void OnSoundEffectVolumeChanged(ref SetSoundEffectVolumeEvent evt)
    {
        try
        {
            var clampedVolume = Math.Clamp(evt.Volume, 0f, 1f);
            _audioEngine.SoundEffectVolume = clampedVolume;
            _logger.Debug("Sound effect volume set to {Volume}", clampedVolume);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling SetSoundEffectVolumeEvent");
        }
    }

    /// <summary>
    ///     Disposes the system and unsubscribes from events.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                EventBus.Unsubscribe<SetMasterVolumeEvent>(OnMasterVolumeChanged);
                EventBus.Unsubscribe<SetMusicVolumeEvent>(OnMusicVolumeChanged);
                EventBus.Unsubscribe<SetSoundEffectVolumeEvent>(OnSoundEffectVolumeChanged);
            }

            _disposed = true;
        }
    }
}
