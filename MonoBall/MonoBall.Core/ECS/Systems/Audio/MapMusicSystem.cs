using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Components.Audio;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Events.Audio;
using MonoBall.Core.Scenes;
using Serilog;

namespace MonoBall.Core.ECS.Systems.Audio;

/// <summary>
///     System that manages map background music based on map transitions.
/// </summary>
public class MapMusicSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly ILogger _logger;
    private readonly QueryDescription _mapQuery;
    private readonly ISceneManager _sceneManager;
    private string? _currentMapMusicId;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the MapMusicSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="sceneManager">The scene manager for checking loading scene state.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public MapMusicSystem(World world, ISceneManager sceneManager, ILogger logger)
        : base(world)
    {
        _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Cache QueryDescription in constructor (required by .cursorrules)
        _mapQuery = new QueryDescription().WithAll<MapComponent>();

        EventBus.Subscribe<MapTransitionEvent>(OnMapTransition);
        EventBus.Subscribe<GameEnteredEvent>(OnGameEntered);
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
    public int Priority => SystemPriority.Audio;

    /// <summary>
    ///     Handles MapTransitionEvent by resolving music from map definition and playing it.
    /// </summary>
    /// <param name="evt">The map transition event.</param>
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
                _logger.Debug("MapTransitionEvent has empty TargetMapId, skipping music change");
                return;
            }

            // Don't play music if loading scene is active (game still initializing)
            if (_sceneManager.IsLoadingSceneActive())
            {
                _logger.Debug(
                    "Loading scene is active, deferring music for map {TargetMapId}",
                    evt.TargetMapId
                );
                return;
            }

            // Query map entity for MusicComponent (allows runtime modification)
            var mapEntity = GetMapEntity(evt.TargetMapId);
            if (!mapEntity.HasValue)
            {
                _logger.Warning("Map entity not found for {MapId}", evt.TargetMapId);
                return;
            }

            if (!World.Has<MusicComponent>(mapEntity.Value))
            {
                _logger.Debug(
                    "Map {TargetMapId} has no MusicComponent, skipping music change",
                    evt.TargetMapId
                );
                return;
            }

            ref var musicComponent = ref World.Get<MusicComponent>(mapEntity.Value);
            if (string.IsNullOrEmpty(musicComponent.AudioId))
            {
                _logger.Debug(
                    "Map {TargetMapId} has empty AudioId in MusicComponent, skipping music change",
                    evt.TargetMapId
                );
                return;
            }

            var musicId = musicComponent.AudioId;
            var fadeDuration = musicComponent.FadeDuration;

            if (musicId == _currentMapMusicId)
            {
                _logger.Debug(
                    "Map {TargetMapId} already playing music {MusicId}, skipping music change",
                    evt.TargetMapId,
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
                evt.TargetMapId,
                fadeDuration > 0 ? fadeDuration : 0f
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling MapTransitionEvent");
        }
    }

    /// <summary>
    ///     Handles GameEnteredEvent by resolving music from initial map definition and playing it.
    /// </summary>
    /// <param name="evt">The game entered event.</param>
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

            // Don't play music if loading scene is active (game still initializing)
            if (_sceneManager.IsLoadingSceneActive())
            {
                _logger.Debug(
                    "Loading scene is active, deferring music for map {InitialMapId}",
                    evt.InitialMapId
                );
                return;
            }

            // Query map entity for MusicComponent (allows runtime modification)
            var mapEntity = GetMapEntity(evt.InitialMapId);
            if (!mapEntity.HasValue)
            {
                _logger.Warning("Map entity not found for {MapId}", evt.InitialMapId);
                return;
            }

            if (!World.Has<MusicComponent>(mapEntity.Value))
            {
                _logger.Debug(
                    "Initial map {InitialMapId} has no MusicComponent, skipping music",
                    evt.InitialMapId
                );
                return;
            }

            ref var musicComponent = ref World.Get<MusicComponent>(mapEntity.Value);
            if (string.IsNullOrEmpty(musicComponent.AudioId))
            {
                _logger.Debug(
                    "Initial map {InitialMapId} has empty AudioId in MusicComponent, skipping music",
                    evt.InitialMapId
                );
                return;
            }

            var musicId = musicComponent.AudioId;
            var fadeDuration = musicComponent.FadeDuration;

            // Prevent duplicate playback if already playing the same track
            if (musicId == _currentMapMusicId)
            {
                _logger.Debug(
                    "Initial map {InitialMapId} already playing music {MusicId}, skipping music change",
                    evt.InitialMapId,
                    musicId
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
                evt.InitialMapId,
                fadeDuration > 0 ? fadeDuration : 0f
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling GameEnteredEvent");
        }
    }

    /// <summary>
    ///     Gets the map entity for the specified map ID by querying MapComponent.
    /// </summary>
    /// <param name="mapId">The map ID to find.</param>
    /// <returns>The map entity if found, null otherwise.</returns>
    private Entity? GetMapEntity(string mapId)
    {
        Entity? foundEntity = null;
        World.Query(
            in _mapQuery,
            (Entity entity, ref MapComponent map) =>
            {
                if (map.MapId == mapId)
                    foundEntity = entity;
            }
        );
        return foundEntity;
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
                EventBus.Unsubscribe<MapTransitionEvent>(OnMapTransition);
                EventBus.Unsubscribe<GameEnteredEvent>(OnGameEntered);
            }

            _disposed = true;
        }
    }
}
