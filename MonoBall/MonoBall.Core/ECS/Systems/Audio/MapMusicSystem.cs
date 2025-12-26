using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Components.Audio;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Events.Audio;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems.Audio
{
    /// <summary>
    /// System that manages map background music based on map transitions.
    /// </summary>
    public class MapMusicSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
    {
        private readonly DefinitionRegistry _registry;
        private readonly QueryDescription _mapMusicQuery;
        private readonly ILogger _logger;
        private string? _currentMapMusicId;
        private bool _disposed = false;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.Audio;

        /// <summary>
        /// Initializes a new instance of the MapMusicSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="registry">The definition registry.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public MapMusicSystem(World world, DefinitionRegistry registry, ILogger logger)
            : base(world)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Cache QueryDescription in constructor (required by .cursorrules)
            _mapMusicQuery = new QueryDescription().WithAll<MapComponent, MusicComponent>();

            EventBus.Subscribe<MapTransitionEvent>(OnMapTransition);
            EventBus.Subscribe<GameEnteredEvent>(OnGameEntered);
        }

        private void OnMapTransition(ref MapTransitionEvent evt)
        {
            try
            {
                _logger.Debug(
                    "Received MapTransitionEvent from {SourceMapId} to {TargetMapId}",
                    evt.SourceMapId ?? "(null)",
                    evt.TargetMapId ?? "(null)"
                );

                if (string.IsNullOrEmpty(evt.TargetMapId))
                {
                    _logger.Debug(
                        "MapTransitionEvent has empty TargetMapId, skipping music change"
                    );
                    return;
                }

                // Copy event data to avoid ref parameter issues in lambda
                string targetMapId = evt.TargetMapId;

                // Query for target map's music component
                string? musicId = null;
                float fadeDuration = 0f;

                World.Query(
                    in _mapMusicQuery,
                    (ref MapComponent map, ref MusicComponent music) =>
                    {
                        if (map.MapId == targetMapId)
                        {
                            musicId = music.AudioId;
                            fadeDuration = music.FadeDuration > 0 ? music.FadeDuration : 0f;
                        }
                    }
                );

                if (string.IsNullOrEmpty(musicId))
                {
                    _logger.Debug(
                        "Map {TargetMapId} has no music component, skipping music change",
                        targetMapId
                    );
                    return;
                }

                if (musicId == _currentMapMusicId)
                {
                    _logger.Debug(
                        "Map {TargetMapId} already playing music {MusicId}, skipping music change",
                        targetMapId,
                        musicId
                    );
                    return;
                }

                var playMusicEvent = new PlayMusicEvent
                {
                    AudioId = musicId,
                    Loop = true,
                    FadeInDuration = fadeDuration > 0 ? fadeDuration : 0f,
                    Crossfade = false, // Use sequential fade (FadeOutAndPlay) instead of crossfade
                };
                EventBus.Send(ref playMusicEvent);
                _currentMapMusicId = musicId;

                _logger.Information(
                    "Playing music {MusicId} for map {TargetMapId} (fade: {FadeDuration}s)",
                    musicId,
                    targetMapId,
                    fadeDuration > 0 ? fadeDuration : 0f
                );
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling MapTransitionEvent");
            }
        }

        private void OnGameEntered(ref GameEnteredEvent evt)
        {
            try
            {
                _logger.Debug(
                    "Received GameEnteredEvent for initial map {InitialMapId}",
                    evt.InitialMapId ?? "(null)"
                );

                if (string.IsNullOrEmpty(evt.InitialMapId))
                {
                    _logger.Debug("GameEnteredEvent has empty InitialMapId, skipping music");
                    return;
                }

                // Copy event data to avoid ref parameter issues in lambda
                string initialMapId = evt.InitialMapId;

                // Query for initial map's music component
                string? musicId = null;
                float fadeDuration = 0f;

                World.Query(
                    in _mapMusicQuery,
                    (ref MapComponent map, ref MusicComponent music) =>
                    {
                        if (map.MapId == initialMapId)
                        {
                            musicId = music.AudioId;
                            fadeDuration = music.FadeDuration > 0 ? music.FadeDuration : 0f;
                        }
                    }
                );

                if (string.IsNullOrEmpty(musicId))
                {
                    _logger.Debug(
                        "Initial map {InitialMapId} has no music component, skipping music",
                        initialMapId
                    );
                    return;
                }

                var playMusicEvent = new PlayMusicEvent
                {
                    AudioId = musicId,
                    Loop = true,
                    FadeInDuration = fadeDuration,
                    Crossfade = false,
                };
                EventBus.Send(ref playMusicEvent);
                _currentMapMusicId = musicId;

                _logger.Information(
                    "Playing music {MusicId} for initial map {InitialMapId} (fade: {FadeDuration}s)",
                    musicId,
                    initialMapId,
                    fadeDuration > 0 ? fadeDuration : 0f
                );
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling GameEnteredEvent");
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
                    EventBus.Unsubscribe<MapTransitionEvent>(OnMapTransition);
                    EventBus.Unsubscribe<GameEnteredEvent>(OnGameEntered);
                }
                _disposed = true;
            }
        }
    }
}
