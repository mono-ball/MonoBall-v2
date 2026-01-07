using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.UI.Windows.Animations;
using MonoBall.Core.UI.Windows.Animations.Events;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System responsible for managing popup lifecycle based on map transitions.
///     Listens to MapTransitionEvent and GameEnteredEvent, resolves map definitions, and creates popup entities/scenes.
/// </summary>
public class MapPopupSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly IConstantsService _constants;
    private readonly ILogger _logger;

    // Cached query descriptions
    private readonly QueryDescription _mapQuery = new QueryDescription().WithAll<MapComponent>();

    private readonly IModManager _modManager;
    private readonly ISceneManager _sceneManager;
    private readonly List<IDisposable> _subscriptions = new();
    private string? _currentMapSectionId;
    private Entity? _currentPopupEntity;
    private Entity? _currentPopupSceneEntity;
    private bool _disposed;

    // Flag to prevent duplicate event processing during same frame
    // This prevents race conditions if multiple events fire in quick succession
    private bool _isProcessingPopupEvent;

    /// <summary>
    ///     Initializes a new instance of the MapPopupSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="sceneManager">The scene manager for creating/destroying scenes.</param>
    /// <param name="modManager">The mod manager for accessing definitions.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <param name="constants">The constants service for accessing game constants.</param>
    public MapPopupSystem(
        World world,
        ISceneManager sceneManager,
        IModManager modManager,
        ILogger logger,
        IConstantsService constants
    )
        : base(world)
    {
        _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));

        // Subscribe to MapTransitionEvent and GameEnteredEvent directly
        _subscriptions.Add(EventBus.Subscribe<MapTransitionEvent>(OnMapTransition));
        _subscriptions.Add(EventBus.Subscribe<GameEnteredEvent>(OnGameEntered));

        // Subscribe to WindowAnimationDestroyEvent to properly destroy popups when animation completes
        // This is CRITICAL - without this subscription, popups are never destroyed and accumulate
        _subscriptions.Add(
            EventBus.Subscribe<WindowAnimationDestroyEvent>(OnWindowAnimationDestroy)
        );
    }

    /// <summary>
    ///     Disposes the system and unsubscribes from events.
    /// </summary>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.MapPopup;

    /// <summary>
    ///     Handles GameEnteredEvent by showing popup for the initial map.
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

            // Prevent duplicate processing if another event is being handled
            if (_isProcessingPopupEvent)
            {
                _logger.Debug("Already processing popup event, skipping GameEnteredEvent");
                return;
            }

            if (string.IsNullOrEmpty(evt.InitialMapId))
            {
                _logger.Debug("GameEnteredEvent has empty InitialMapId, skipping popup");
                return;
            }

            // Note: GameEnteredEvent is now fired from GameInitializationService.MarkComplete()
            // after loading completes, so we don't need to check IsLoadingSceneActive() here.
            // The popup will be created and will become visible when the loading scene is dismissed.

            // Set processing flag to prevent re-entrancy
            _isProcessingPopupEvent = true;
            try
            {
                // Show popup for the initial map
                ShowPopupForMap(evt.InitialMapId);
            }
            finally
            {
                _isProcessingPopupEvent = false;
            }
        }
        catch (Exception ex)
        {
            _isProcessingPopupEvent = false;
            _logger.Error(ex, "Error handling GameEnteredEvent");
        }
    }

    /// <summary>
    ///     Handles WindowAnimationDestroyEvent to destroy popup when animation completes.
    ///     This is the proper cleanup path for popups - called when DestroyOnComplete animation finishes.
    /// </summary>
    /// <param name="evt">The window animation destroy event.</param>
    private void OnWindowAnimationDestroy(ref WindowAnimationDestroyEvent evt)
    {
        try
        {
            // Only handle events for our tracked popup entity
            if (!_currentPopupEntity.HasValue)
            {
                _logger.Debug(
                    "Received WindowAnimationDestroyEvent but no current popup entity tracked"
                );
                return;
            }

            // Check if this destroy event is for our popup (by WindowEntity, which is the popup entity)
            if (evt.WindowEntity.Id != _currentPopupEntity.Value.Id)
            {
                _logger.Debug(
                    "WindowAnimationDestroyEvent for entity {EntityId} does not match current popup {PopupEntityId}",
                    evt.WindowEntity.Id,
                    _currentPopupEntity.Value.Id
                );
                return;
            }

            _logger.Debug(
                "Received WindowAnimationDestroyEvent for popup entity {PopupEntityId}",
                evt.WindowEntity.Id
            );

            // Verify popup entity is still alive before attempting destruction
            if (!World.IsAlive(_currentPopupEntity.Value))
            {
                _logger.Debug(
                    "Popup entity {PopupEntityId} is already destroyed, clearing tracked reference",
                    evt.WindowEntity.Id
                );
                _currentPopupEntity = null;
                _currentPopupSceneEntity = null;
                _currentMapSectionId = null;
                return;
            }

            // Destroy the popup (this handles scene cleanup too)
            DestroyPopup(_currentPopupEntity.Value);

            _logger.Information(
                "Destroyed popup entity {PopupEntityId} via WindowAnimationDestroyEvent",
                evt.WindowEntity.Id
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling WindowAnimationDestroyEvent");
        }
    }

    /// <summary>
    ///     Handles MapTransitionEvent by resolving map section and theme definitions, then creating popup.
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

            // Prevent duplicate processing if another event is being handled
            if (_isProcessingPopupEvent)
            {
                _logger.Debug("Already processing popup event, skipping MapTransitionEvent");
                return;
            }

            if (string.IsNullOrEmpty(evt.TargetMapId))
            {
                _logger.Debug("MapTransitionEvent has empty TargetMapId, skipping popup");
                return;
            }

            // Don't show popup if loading scene is active (game still initializing)
            if (_sceneManager.IsLoadingSceneActive())
            {
                _logger.Debug(
                    "Loading scene is active, deferring popup for map {TargetMapId}",
                    evt.TargetMapId
                );
                return;
            }

            // Set processing flag to prevent re-entrancy
            _isProcessingPopupEvent = true;
            try
            {
                // Show popup for the target map
                ShowPopupForMap(evt.TargetMapId);
            }
            finally
            {
                _isProcessingPopupEvent = false;
            }
        }
        catch (Exception ex)
        {
            _isProcessingPopupEvent = false;
            _logger.Error(ex, "Error handling MapTransitionEvent");
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
    ///     Shows a popup for the specified map by querying MapSectionComponent and resolving definitions, then creating popup
    ///     entity and scene.
    /// </summary>
    /// <param name="mapId">The map ID to show a popup for.</param>
    private void ShowPopupForMap(string mapId)
    {
        // Query map entity for MapSectionComponent (allows runtime modification)
        var mapEntity = GetMapEntity(mapId);
        if (!mapEntity.HasValue)
        {
            _logger.Warning("Map entity not found for {MapId}", mapId);
            return;
        }

        // Check if map name display is enabled (still need definition for this)
        var mapDefinition = _modManager.GetDefinition<MapDefinition>(mapId);
        if (mapDefinition == null)
        {
            _logger.Warning("Map definition not found for {MapId}", mapId);
            return;
        }

        if (!mapDefinition.ShowMapName)
        {
            _logger.Debug("Map {MapId} has ShowMapName=false, skipping popup", mapId);
            return;
        }

        // Get MapSectionId from MapSectionComponent (allows runtime modification)
        if (!World.Has<MapSectionComponent>(mapEntity.Value))
        {
            _logger.Debug("Map {MapId} has no MapSectionComponent, skipping popup", mapId);
            return;
        }

        ref var mapSectionComponent = ref World.Get<MapSectionComponent>(mapEntity.Value);
        if (string.IsNullOrEmpty(mapSectionComponent.MapSectionId))
        {
            _logger.Debug(
                "Map {MapId} has empty MapSectionId in MapSectionComponent, skipping popup",
                mapId
            );
            return;
        }

        var mapSectionId = mapSectionComponent.MapSectionId;
        var popupThemeId = mapSectionComponent.PopupThemeId;

        // Resolve MapSectionDefinition to get display name
        var mapSectionDefinition = _modManager.GetDefinition<MapSectionDefinition>(mapSectionId);
        if (mapSectionDefinition == null)
        {
            _logger.Warning("MapSection definition not found for {MapSectionId}", mapSectionId);
            return;
        }

        // Validate popup theme exists
        if (string.IsNullOrEmpty(popupThemeId))
        {
            _logger.Warning(
                "MapSection {MapSectionId} has no PopupThemeId in component, skipping popup",
                mapSectionId
            );
            return;
        }

        var popupThemeDefinition = _modManager.GetDefinition<PopupThemeDefinition>(popupThemeId);
        if (popupThemeDefinition == null)
        {
            _logger.Warning(
                "PopupTheme definition not found for {ThemeId}, skipping popup",
                popupThemeId
            );
            return;
        }

        // Prevent duplicate popup if already showing the same map section
        if (mapSectionId == _currentMapSectionId)
        {
            _logger.Debug(
                "Map {MapId} already showing popup for map section {MapSectionId}, skipping popup",
                mapId,
                mapSectionId
            );
            return;
        }

        // Create popup with resolved data
        CreatePopup(mapSectionId, mapSectionDefinition.Name, popupThemeId);
    }

    /// <summary>
    ///     Creates a popup entity and scene for the specified map section.
    /// </summary>
    /// <param name="mapSectionId">The map section ID.</param>
    /// <param name="mapSectionName">The map section name to display.</param>
    /// <param name="themeId">The popup theme ID.</param>
    private void CreatePopup(string mapSectionId, string mapSectionName, string themeId)
    {
        if (string.IsNullOrEmpty(mapSectionName))
        {
            _logger.Warning("MapSectionName is empty, skipping popup");
            return;
        }

        _logger.Debug(
            "Creating popup for {MapSectionName} with theme {ThemeId}",
            mapSectionName,
            themeId
        );

        // Cancel existing popup if present
        if (_currentPopupEntity.HasValue && World.IsAlive(_currentPopupEntity.Value))
        {
            _logger.Debug("Cancelling existing popup");
            DestroyPopup(_currentPopupEntity.Value);
        }

        // Look up PopupThemeDefinition to get background and outline IDs
        var popupTheme = _modManager.GetDefinition<PopupThemeDefinition>(themeId);
        if (popupTheme == null)
        {
            _logger.Warning("PopupTheme definition not found for {ThemeId}", themeId);
            return;
        }

        // Get outline definition first (needed for dimension calculation)
        var outlineDef = _modManager.GetDefinition<PopupOutlineDefinition>(popupTheme.Outline);
        if (outlineDef == null)
        {
            _logger.Warning("Outline definition not found for {OutlineId}", popupTheme.Outline);
            return;
        }

        // Calculate popup dimensions (fixed size like pokeemerald)
        // Background is always 80x24 at 1x scale, plus border tiles
        var tileSize = outlineDef.IsTileSheet ? outlineDef.TileWidth : 8;
        float popupHeight = _constants.Get<int>("PopupBackgroundHeight") + tileSize * 2; // Background + border on top and bottom

        // Create popup scene entity first (before popup entity so we can reference it)
        var sceneComponent = new SceneComponent
        {
            SceneId = $"map:popup:{Guid.NewGuid()}",
            Priority = ScenePriorities.GameScene + 10, // 60
            CameraMode = SceneCameraMode.GameCamera,
            BlocksUpdate = false,
            BlocksDraw = false,
            IsActive = true,
            IsPaused = false,
            BackgroundColor = Color.Transparent, // Map popup is transparent overlay
        };

        var popupSceneComponent = new MapPopupSceneComponent();

        Entity popupSceneEntity;
        try
        {
            popupSceneEntity = _sceneManager.CreateScene(sceneComponent, popupSceneComponent);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create popup scene for {MapSectionName}", mapSectionName);
            return;
        }

        _currentPopupSceneEntity = popupSceneEntity;

        // Create popup entity
        var popupComponent = new MapPopupComponent
        {
            MapSectionName = mapSectionName,
            ThemeId = themeId,
            BackgroundId = popupTheme.Background,
            OutlineId = popupTheme.Outline,
            SceneEntity = popupSceneEntity, // Store scene entity reference
        };

        // Create window animation component using helper
        var animationConfig = WindowAnimationHelper.CreateSlideDownUpAnimation(
            0.4f, // GBA-accurate slide in duration
            2.5f, // GBA-accurate display duration
            0.4f, // GBA-accurate slide out duration
            popupHeight
        );

        // Create entity first, then set WindowEntity reference
        _currentPopupEntity = World.Create(popupComponent);

        // Add explicit scene ownership component for queryable scene membership
        World.Add(
            _currentPopupEntity.Value,
            new SceneOwnershipComponent { SceneEntity = popupSceneEntity }
        );

        var windowAnim = new WindowAnimationComponent
        {
            State = WindowAnimationState.NotStarted,
            ElapsedTime = 0f,
            Config = animationConfig,
            PositionOffset = new Vector2(0, -popupHeight), // Start off-screen
            Scale = 1.0f,
            Opacity = 1.0f,
            WindowEntity = _currentPopupEntity.Value, // Set to popup entity itself
        };

        // Add animation component to the entity
        World.Add(_currentPopupEntity.Value, windowAnim);

        // Track current map section ID to prevent duplicates
        _currentMapSectionId = mapSectionId;

        _logger.Information(
            "Created popup entity {PopupEntityId} and scene entity {SceneEntityId} for {MapSectionName} (height: {Height})",
            _currentPopupEntity.Value.Id,
            popupSceneEntity.Id,
            mapSectionName,
            popupHeight
        );
    }

    /// <summary>
    ///     Destroys a popup entity and its associated scene entity.
    /// </summary>
    /// <param name="popupEntity">The popup entity to destroy.</param>
    private void DestroyPopup(Entity popupEntity)
    {
        _logger.Debug("Destroying popup entity {PopupEntityId}", popupEntity.Id);

        // Get scene entity from popup component before destroying
        // Fail fast if popup entity doesn't exist or doesn't have required component
        if (!World.IsAlive(popupEntity))
            throw new InvalidOperationException(
                $"Cannot destroy popup entity {popupEntity.Id}: Entity is not alive."
            );

        if (!World.Has<MapPopupComponent>(popupEntity))
            throw new InvalidOperationException(
                $"Cannot destroy popup entity {popupEntity.Id}: Entity does not have MapPopupComponent. "
                    + "Cannot determine scene entity to destroy."
            );

        ref var popupComponent = ref World.Get<MapPopupComponent>(popupEntity);
        var sceneEntityToDestroy = popupComponent.SceneEntity;

        // Validate scene entity exists and is alive
        if (!World.IsAlive(sceneEntityToDestroy))
            throw new InvalidOperationException(
                $"Cannot destroy popup scene entity for popup {popupEntity.Id}: "
                    + $"Scene entity {sceneEntityToDestroy.Id} is not alive. "
                    + "This indicates the scene entity was already destroyed or never created properly."
            );

        // Destroy popup scene entity first (before destroying popup entity)
        _sceneManager.DestroyScene(sceneEntityToDestroy);

        // Destroy popup entity
        World.Destroy(popupEntity);

        // Clear tracked entities
        if (_currentPopupEntity.HasValue && _currentPopupEntity.Value.Id == popupEntity.Id)
        {
            _currentPopupEntity = null;
            _currentMapSectionId = null;
        }

        if (
            _currentPopupSceneEntity.HasValue
            && _currentPopupSceneEntity.Value.Id == sceneEntityToDestroy.Id
        )
            _currentPopupSceneEntity = null;
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
                foreach (var subscription in _subscriptions)
                    subscription.Dispose();

            _disposed = true;
        }
    }
}
