using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.Audio;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Events.Audio;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems.Audio
{
    /// <summary>
    /// System that handles music playback events and manages music state.
    /// </summary>
    public class MusicPlaybackSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
    {
        private readonly DefinitionRegistry _registry;
        private readonly IAudioEngine _audioEngine;
        private readonly ILogger _logger;
        private bool _disposed = false;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.Audio + 5;

        /// <summary>
        /// Initializes a new instance of the MusicPlaybackSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="registry">The definition registry.</param>
        /// <param name="audioEngine">The audio engine for playing music.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public MusicPlaybackSystem(
            World world,
            DefinitionRegistry registry,
            IAudioEngine audioEngine,
            ILogger logger
        )
            : base(world)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            EventBus.Subscribe<PlayMusicEvent>(OnPlayMusic);
            EventBus.Subscribe<StopMusicEvent>(OnStopMusic);
        }

        private void OnPlayMusic(ref PlayMusicEvent evt)
        {
            try
            {
                if (string.IsNullOrEmpty(evt.AudioId))
                {
                    _logger.Warning("PlayMusicEvent received with empty AudioId");
                    return;
                }

                // Get definition
                var definition = _registry.GetById<AudioDefinition>(evt.AudioId);
                if (definition == null)
                {
                    _logger.Warning("Audio definition not found: {AudioId}", evt.AudioId);
                    return;
                }

                // Determine loop and fade settings
                bool loop = evt.Loop || definition.Loop;
                float fadeIn = evt.FadeInDuration > 0 ? evt.FadeInDuration : definition.FadeIn;

                if (evt.Crossfade)
                {
                    _audioEngine.CrossfadeMusic(evt.AudioId, evt.CrossfadeDuration, loop);
                }
                else
                {
                    _audioEngine.PlayMusic(evt.AudioId, loop, fadeIn);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling PlayMusicEvent for {AudioId}", evt.AudioId);
            }
        }

        private void OnStopMusic(ref StopMusicEvent evt)
        {
            try
            {
                _audioEngine.StopMusic(evt.FadeOutDuration);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling StopMusicEvent");
            }
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <remarks>
        /// Implements IDisposable to properly clean up event subscriptions.
        /// Uses standard dispose pattern without finalizer since only managed resources are disposed.
        /// Uses 'new' keyword because BaseSystem may have a Dispose() method with different signature.
        /// </remarks>
        public new void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    EventBus.Unsubscribe<PlayMusicEvent>(OnPlayMusic);
                    EventBus.Unsubscribe<StopMusicEvent>(OnStopMusic);
                }
                _disposed = true;
            }
        }
    }
}
