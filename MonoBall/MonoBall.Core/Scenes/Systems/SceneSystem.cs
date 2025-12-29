using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Events;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System responsible for managing scene lifecycle, priority stack, and state.
///     Coordinates scene-specific systems for updates and rendering.
/// </summary>
public class SceneSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable, ISceneManager
{
    private readonly QueryDescription _cameraQueryDescription;
    private readonly ISceneSystem? _debugBarSceneSystem;

    // Scene-specific systems (owned by SceneSystem, set by SystemManager)
    // Using ISceneSystem interface for loose coupling
    private readonly ISceneSystem? _gameSceneSystem;

    // Rendering dependencies
    private readonly GraphicsDevice? _graphicsDevice;
    private readonly ISceneSystem? _loadingSceneSystem;

    private readonly ILogger _logger;
    private readonly Dictionary<string, Entity> _sceneIds = new();

    private readonly Dictionary<Entity, int> _sceneInsertionOrder = new();

    private readonly List<Entity> _sceneStack = new();

    // Registry for mapping component types to scene systems
    private readonly Dictionary<Type, ISceneSystem> _sceneSystemRegistry = new();

    private readonly ShaderManagerSystem? _shaderManagerSystem;
    private bool _disposed;
    private ISceneSystem? _mapPopupSceneSystem;
    private ISceneSystem? _messageBoxSceneSystem;
    private int _nextInsertionOrder;

    /// <summary>
    ///     Initializes a new instance of the SceneSystem.
    ///     SceneSystem coordinates scene-specific systems but does not create them.
    ///     Systems are created by SystemManager and passed to SceneSystem.
    /// </summary>
    /// <param name="world">The ECS world. Required.</param>
    /// <param name="logger">The logger for logging operations. Required.</param>
    /// <param name="graphicsDevice">The graphics device for rendering. Required.</param>
    /// <param name="shaderManagerSystem">The shader manager system. Optional - only needed if shader rendering is used.</param>
    /// <param name="gameSceneSystem">The game scene system. Typically provided by SystemManager.</param>
    /// <param name="loadingSceneSystem">The loading scene system. Typically provided by SystemManager.</param>
    /// <param name="debugBarSceneSystem">The debug bar scene system. Typically provided by SystemManager.</param>
    /// <param name="mapPopupSceneSystem">
    ///     The map popup scene system. Typically set via SetMapPopupSceneSystem() after
    ///     construction.
    /// </param>
    /// <remarks>
    ///     Scene systems are optional parameters to allow flexible initialization.
    ///     In practice, SystemManager provides all scene systems except MapPopupSceneSystem,
    ///     which is set after SceneSystem construction (due to circular dependency).
    /// </remarks>
    public SceneSystem(
        World world,
        ILogger logger,
        GraphicsDevice graphicsDevice,
        ShaderManagerSystem? shaderManagerSystem = null,
        ISceneSystem? gameSceneSystem = null,
        ISceneSystem? loadingSceneSystem = null,
        ISceneSystem? debugBarSceneSystem = null,
        ISceneSystem? mapPopupSceneSystem = null,
        ISceneSystem? messageBoxSceneSystem = null
    )
        : base(world)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _shaderManagerSystem = shaderManagerSystem;
        _gameSceneSystem = gameSceneSystem;
        _loadingSceneSystem = loadingSceneSystem;
        _debugBarSceneSystem = debugBarSceneSystem;
        _mapPopupSceneSystem = mapPopupSceneSystem;
        _messageBoxSceneSystem = messageBoxSceneSystem;

        // Register scene systems in registry
        if (gameSceneSystem != null)
            RegisterSceneSystem(typeof(GameSceneComponent), gameSceneSystem);
        if (loadingSceneSystem != null)
            RegisterSceneSystem(typeof(LoadingSceneComponent), loadingSceneSystem);
        if (debugBarSceneSystem != null)
            RegisterSceneSystem(typeof(DebugBarSceneComponent), debugBarSceneSystem);
        if (mapPopupSceneSystem != null)
            RegisterSceneSystem(typeof(MapPopupSceneComponent), mapPopupSceneSystem);
        if (messageBoxSceneSystem != null)
            RegisterSceneSystem(typeof(MessageBoxSceneComponent), messageBoxSceneSystem);

        // Subscribe to SceneMessageEvent for inter-scene communication
        EventBus.Subscribe<SceneMessageEvent>(OnSceneMessage);
        _cameraQueryDescription = new QueryDescription().WithAll<CameraComponent>();
    }

    /// <summary>
    ///     Gets the loading scene system (for progress updates during initialization).
    ///     Returns concrete type for external systems that need LoadingSceneSystem-specific functionality
    ///     (e.g., GameInitializationService.EnqueueProgress).
    /// </summary>
    /// <remarks>
    ///     This property exposes the concrete type for external use, but internal coordination
    ///     uses ISceneSystem interface to maintain loose coupling.
    /// </remarks>
    public LoadingSceneSystem? LoadingSceneSystem => _loadingSceneSystem as LoadingSceneSystem;

    /// <summary>
    ///     Disposes of the system and unsubscribes from events.
    /// </summary>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.Scene;

    /// <summary>
    ///     Creates a scene entity and adds it to the scene stack.
    /// </summary>
    /// <param name="sceneComponent">The scene component data.</param>
    /// <param name="additionalComponents">Additional components to add to the scene entity.</param>
    /// <returns>The created scene entity.</returns>
    public Entity CreateScene(SceneComponent sceneComponent, params object[] additionalComponents)
    {
        if (string.IsNullOrEmpty(sceneComponent.SceneId))
            throw new ArgumentException(
                "Scene ID cannot be null or empty.",
                nameof(sceneComponent)
            );

        if (_sceneIds.ContainsKey(sceneComponent.SceneId))
            throw new InvalidOperationException(
                $"Scene with ID '{sceneComponent.SceneId}' already exists."
            );

        // Fire SceneCreatingEvent to allow systems to prepare or cancel creation
        var cancelled = SceneEventHelper.FireSceneCreating(sceneComponent.SceneId);
        if (cancelled)
        {
            _logger.Information("Scene creation cancelled for '{SceneId}'", sceneComponent.SceneId);
            throw new InvalidOperationException(
                $"Scene creation was cancelled for '{sceneComponent.SceneId}'."
            );
        }

        // Validate BackgroundColor is set
        if (!sceneComponent.BackgroundColor.HasValue)
            throw new ArgumentException(
                "BackgroundColor must be set on SceneComponent. All scenes must specify a background color.",
                nameof(sceneComponent)
            );

        // Validate CameraEntityId if SceneCamera mode is specified
        if (sceneComponent.CameraMode == SceneCameraMode.SceneCamera)
        {
            if (!sceneComponent.CameraEntityId.HasValue)
                throw new ArgumentException(
                    "CameraEntityId must be set when CameraMode is SceneCamera.",
                    nameof(sceneComponent)
                );

            // Verify the camera entity exists and has CameraComponent
            // Query for the camera entity by ID
            var cameraEntityId = sceneComponent.CameraEntityId.Value;
            var cameraFound = false;

            World.Query(
                in _cameraQueryDescription,
                (Entity entity, ref CameraComponent _) =>
                {
                    if (entity.Id == cameraEntityId)
                        cameraFound = true;
                }
            );

            if (!cameraFound)
                throw new ArgumentException(
                    $"Camera entity {cameraEntityId} does not exist or does not have CameraComponent.",
                    nameof(sceneComponent)
                );
        }

        // Create scene entity with SceneComponent
        // Note: Arch ECS handles struct components correctly, but we ensure the component
        // is set properly by using Set() after creation to avoid any potential boxing issues
        var sceneEntity = World.Create(sceneComponent);

        // Ensure component is stored correctly (defensive check)
        ref var storedSceneComponent = ref World.Get<SceneComponent>(sceneEntity);
        storedSceneComponent = sceneComponent;

        // Add additional components if provided
        // Note: We handle scene type components specifically
        if (additionalComponents != null && additionalComponents.Length > 0)
            foreach (var component in additionalComponents)
                if (component is GameSceneComponent gameSceneComp)
                    World.Add(sceneEntity, gameSceneComp);
                else if (component is DebugBarSceneComponent debugBarSceneComp)
                    World.Add(sceneEntity, debugBarSceneComp);
                else if (component is MapPopupSceneComponent mapPopupSceneComp)
                    World.Add(sceneEntity, mapPopupSceneComp);
                else if (component is LoadingSceneComponent loadingSceneComp)
                    World.Add(sceneEntity, loadingSceneComp);
                else if (component is LoadingProgressComponent loadingProgressComp)
                    World.Add(sceneEntity, loadingProgressComp);
                else if (component is MessageBoxSceneComponent messageBoxSceneComp)
                    World.Add(sceneEntity, messageBoxSceneComp);

        // Add other component types as needed in the future
        // Add to scene stack and ID lookup
        _sceneStack.Add(sceneEntity);
        _sceneIds[sceneComponent.SceneId] = sceneEntity;
        _sceneInsertionOrder[sceneEntity] = _nextInsertionOrder++;

        // Sort stack by priority (highest first)
        SortSceneStack();

        // Get scene type from marker components
        var sceneType = SceneEventHelper.GetSceneType(World, sceneEntity);

        // Fire SceneCreatedEvent using helper
        SceneEventHelper.FireSceneCreated(sceneEntity, ref sceneComponent, sceneType);
        _logger.Information(
            "Created scene '{SceneId}' with priority {Priority}",
            sceneComponent.SceneId,
            sceneComponent.Priority
        );

        return sceneEntity;
    }

    /// <summary>
    ///     Destroys a scene entity and removes it from the scene stack.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to destroy.</param>
    public void DestroyScene(Entity sceneEntity)
    {
        if (!World.IsAlive(sceneEntity))
        {
            _logger.Warning("Attempted to destroy scene entity that is not alive");
            return;
        }

        ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity);
        var sceneId = sceneComponent.SceneId;

        // Get scene type from marker components
        var sceneType = SceneEventHelper.GetSceneType(World, sceneEntity);

        // Fire SceneDestroyedEvent using helper
        SceneEventHelper.FireSceneDestroyed(sceneEntity, ref sceneComponent, sceneType);

        // Remove from stack and ID lookup
        _sceneStack.Remove(sceneEntity);
        _sceneIds.Remove(sceneId);
        _sceneInsertionOrder.Remove(sceneEntity);

        // Destroy entity
        World.Destroy(sceneEntity);

        _logger.Information("Destroyed scene '{SceneId}'", sceneId);
    }

    /// <summary>
    ///     Destroys a scene entity by scene ID.
    /// </summary>
    /// <param name="sceneId">The scene ID.</param>
    public void DestroyScene(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId))
            throw new ArgumentException("Scene ID cannot be null or empty.", nameof(sceneId));

        if (!_sceneIds.TryGetValue(sceneId, out var sceneEntity))
        {
            _logger.Warning("Scene with ID '{SceneId}' not found", sceneId);
            return;
        }

        DestroyScene(sceneEntity);
    }

    /// <summary>
    ///     Checks if a loading scene is currently active.
    /// </summary>
    /// <returns>True if a loading scene is active, false otherwise.</returns>
    public bool IsLoadingSceneActive()
    {
        foreach (var sceneEntity in _sceneStack)
        {
            if (!World.IsAlive(sceneEntity))
                continue;

            if (!World.Has<LoadingSceneComponent>(sceneEntity))
                continue;

            ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity);
            if (sceneComponent.IsActive && !sceneComponent.IsPaused)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Sets the map popup scene system.
    ///     Called by SystemManager after creating MapPopupSceneSystem (which needs ISceneManager).
    /// </summary>
    /// <param name="mapPopupSceneSystem">The map popup scene system.</param>
    public void SetMapPopupSceneSystem(ISceneSystem mapPopupSceneSystem)
    {
        _mapPopupSceneSystem =
            mapPopupSceneSystem ?? throw new ArgumentNullException(nameof(mapPopupSceneSystem));
        RegisterSceneSystem(typeof(MapPopupSceneComponent), mapPopupSceneSystem);
    }

    /// <summary>
    ///     Sets the message box scene system.
    ///     Called by SystemManager after creating MessageBoxSceneSystem (which needs ISceneManager).
    /// </summary>
    /// <param name="messageBoxSceneSystem">The message box scene system.</param>
    public void SetMessageBoxSceneSystem(ISceneSystem messageBoxSceneSystem)
    {
        _messageBoxSceneSystem =
            messageBoxSceneSystem ?? throw new ArgumentNullException(nameof(messageBoxSceneSystem));
        RegisterSceneSystem(typeof(MessageBoxSceneComponent), messageBoxSceneSystem);
    }

    /// <summary>
    ///     Registers a scene system for a specific component type.
    /// </summary>
    /// <param name="componentType">The component type that identifies the scene type.</param>
    /// <param name="sceneSystem">The scene system to register.</param>
    private void RegisterSceneSystem(Type componentType, ISceneSystem sceneSystem)
    {
        if (componentType == null)
            throw new ArgumentNullException(nameof(componentType));
        if (sceneSystem == null)
            throw new ArgumentNullException(nameof(sceneSystem));

        _sceneSystemRegistry[componentType] = sceneSystem;
    }

    /// <summary>
    ///     Gets the scene entity by scene ID.
    /// </summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <returns>The scene entity, or null if not found.</returns>
    public Entity? GetSceneEntity(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId))
            return null;

        _sceneIds.TryGetValue(sceneId, out var sceneEntity);
        return sceneEntity;
    }

    /// <summary>
    ///     Gets the scene stack (priority-ordered list of scene entities).
    /// </summary>
    /// <returns>A read-only list of scene entities, ordered by priority (highest first).</returns>
    public IReadOnlyList<Entity> GetSceneStack()
    {
        return _sceneStack.AsReadOnly();
    }

    /// <summary>
    ///     Sets whether a scene is active.
    /// </summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <param name="active">Whether the scene should be active.</param>
    public void SetSceneActive(string sceneId, bool active)
    {
        var sceneEntity = GetSceneEntity(sceneId);
        if (sceneEntity == null)
        {
            _logger.Warning("Scene with ID '{SceneId}' not found", sceneId);
            return;
        }

        ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity.Value);
        var wasActive = sceneComponent.IsActive;
        sceneComponent.IsActive = active;

        // Fire appropriate event using helper
        if (active && !wasActive)
        {
            SceneEventHelper.FireSceneActivated(sceneEntity.Value, ref sceneComponent);
            _logger.Debug("Activated scene '{SceneId}'", sceneId);
        }
        else if (!active && wasActive)
        {
            SceneEventHelper.FireSceneDeactivated(sceneEntity.Value, ref sceneComponent);
            _logger.Debug("Deactivated scene '{SceneId}'", sceneId);
        }
    }

    /// <summary>
    ///     Sets whether a scene is paused.
    /// </summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <param name="paused">Whether the scene should be paused.</param>
    public void SetScenePaused(string sceneId, bool paused)
    {
        var sceneEntity = GetSceneEntity(sceneId);
        if (sceneEntity == null)
        {
            _logger.Warning("Scene with ID '{SceneId}' not found", sceneId);
            return;
        }

        ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity.Value);
        var wasPaused = sceneComponent.IsPaused;
        sceneComponent.IsPaused = paused;

        // Fire appropriate event using helper
        if (paused && !wasPaused)
        {
            SceneEventHelper.FireScenePaused(sceneEntity.Value, ref sceneComponent);
            _logger.Debug("Paused scene '{SceneId}'", sceneId);
        }
        else if (!paused && wasPaused)
        {
            SceneEventHelper.FireSceneResumed(sceneEntity.Value, ref sceneComponent);
            _logger.Debug("Resumed scene '{SceneId}'", sceneId);
        }
    }

    /// <summary>
    ///     Sets the priority of a scene and re-sorts the scene stack.
    /// </summary>
    /// <param name="sceneId">The scene ID.</param>
    /// <param name="priority">The new priority value.</param>
    public void SetScenePriority(string sceneId, int priority)
    {
        var sceneEntity = GetSceneEntity(sceneId);
        if (sceneEntity == null)
        {
            _logger.Warning("Scene with ID '{SceneId}' not found", sceneId);
            return;
        }

        ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity.Value);
        var oldPriority = sceneComponent.Priority;
        sceneComponent.Priority = priority;

        // Fire priority changed event
        var priorityChangedEvent = new ScenePriorityChangedEvent
        {
            SceneId = sceneId,
            OldPriority = oldPriority,
            NewPriority = priority,
        };
        EventBus.Send(ref priorityChangedEvent);

        // Re-sort scene stack
        SortSceneStack();

        _logger.Debug(
            "Changed priority of scene '{SceneId}' from {OldPriority} to {NewPriority}",
            sceneId,
            oldPriority,
            priority
        );
    }

    /// <summary>
    ///     Handles inter-scene messages.
    /// </summary>
    /// <param name="evt">The scene message event.</param>
    private void OnSceneMessage(ref SceneMessageEvent evt)
    {
        // Handle message based on MessageType
        switch (evt.MessageType.ToLowerInvariant())
        {
            case "pause":
                if (evt.TargetSceneId != null)
                    SetScenePaused(evt.TargetSceneId, true);
                break;

            case "resume":
                if (evt.TargetSceneId != null)
                    SetScenePaused(evt.TargetSceneId, false);
                break;

            case "destroy":
                if (evt.TargetSceneId != null)
                    DestroyScene(evt.TargetSceneId);
                break;

            // Add more message types as needed
            default:
                _logger.Debug(
                    "SceneSystem: Unhandled scene message type '{MessageType}' from '{SourceSceneId}' to '{TargetSceneId}'",
                    evt.MessageType,
                    evt.SourceSceneId,
                    evt.TargetSceneId ?? "all"
                );
                break;
        }
    }

    /// <summary>
    ///     Finds the appropriate scene system for a given scene entity based on its component type.
    ///     Uses registry lookup for efficient O(1) access.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <returns>The scene system for this scene type, or null if not found.</returns>
    private ISceneSystem? FindSceneSystem(Entity sceneEntity)
    {
        // Check component types in order of specificity and lookup in registry
        // This is more efficient than checking World.Has<> multiple times
        if (World.Has<GameSceneComponent>(sceneEntity))
            return _sceneSystemRegistry.TryGetValue(typeof(GameSceneComponent), out var system)
                ? system
                : null;
        if (World.Has<LoadingSceneComponent>(sceneEntity))
            return _sceneSystemRegistry.TryGetValue(typeof(LoadingSceneComponent), out var system)
                ? system
                : null;
        if (World.Has<DebugBarSceneComponent>(sceneEntity))
            return _sceneSystemRegistry.TryGetValue(typeof(DebugBarSceneComponent), out var system)
                ? system
                : null;
        if (World.Has<MapPopupSceneComponent>(sceneEntity))
            return _sceneSystemRegistry.TryGetValue(typeof(MapPopupSceneComponent), out var system)
                ? system
                : null;
        if (World.Has<MessageBoxSceneComponent>(sceneEntity))
            return _sceneSystemRegistry.TryGetValue(
                typeof(MessageBoxSceneComponent),
                out var system
            )
                ? system
                : null;

        return null;
    }

    /// <summary>
    ///     Updates the scene system and coordinates scene-specific systems.
    ///     Iterates through active scenes and calls Update on the appropriate scene system.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Clean up dead entities before processing
        CleanupDeadEntities();

        // Check if updates are blocked
        var isBlocked = IsUpdateBlocked();

        // Copy in parameter to local variable for use in lambda (cannot capture in parameters)
        var dt = deltaTime;

        // Iterate scenes and update them (consistent with Render pattern)
        IterateScenes(
            (sceneEntity, sceneComponent) =>
            {
                // Skip inactive or paused scenes
                if (!sceneComponent.IsActive || sceneComponent.IsPaused)
                    return true; // Continue iterating

                // If updates are blocked, only update loading scenes (they need to process progress queue)
                if (isBlocked && !World.Has<LoadingSceneComponent>(sceneEntity))
                    return true; // Skip non-loading scenes when blocked

                // Skip scenes that block updates (unless it's a loading scene)
                if (sceneComponent.BlocksUpdate && !World.Has<LoadingSceneComponent>(sceneEntity))
                    return true; // Continue iterating

                // Find appropriate scene system and update
                var sceneSystem = FindSceneSystem(sceneEntity);
                sceneSystem?.Update(sceneEntity, dt);

                return true; // Continue iterating
            }
        );

        // Also call ProcessInternal() for systems that need to process queues/entities
        // (e.g., LoadingSceneSystem processes progress queue, MapPopupSceneSystem updates popup animations)
        // These systems query for entities internally, so they need ProcessInternal() called
        if (isBlocked)
        {
            // Only process loading scene and message box scene (they need to process even when blocked)
            _loadingSceneSystem?.ProcessInternal(deltaTime);
            _messageBoxSceneSystem?.ProcessInternal(deltaTime);
        }
        else
        {
            // Process systems that need internal processing
            _loadingSceneSystem?.ProcessInternal(deltaTime);
            _mapPopupSceneSystem?.ProcessInternal(deltaTime);
            _messageBoxSceneSystem?.ProcessInternal(deltaTime);
        }
    }

    /// <summary>
    ///     Checks if any active scene has BlocksUpdate=true.
    ///     Public method for external systems to check if updates are blocked.
    /// </summary>
    /// <returns>True if updates are blocked, false otherwise.</returns>
    public bool IsUpdateBlocked()
    {
        foreach (var sceneEntity in _sceneStack)
        {
            if (!World.IsAlive(sceneEntity))
                continue;

            ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity);
            if (sceneComponent.IsActive && !sceneComponent.IsPaused && sceneComponent.BlocksUpdate)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Gets the list of scene entities that are currently blocking updates.
    ///     Used by systems that need to selectively update entities belonging to blocking scenes.
    /// </summary>
    /// <returns>A list of scene entities that have BlocksUpdate=true and are active.</returns>
    public List<Entity> GetBlockingScenes()
    {
        var blockingScenes = new List<Entity>();
        foreach (var sceneEntity in _sceneStack)
        {
            if (!World.IsAlive(sceneEntity))
                continue;

            ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity);
            if (sceneComponent.IsActive && !sceneComponent.IsPaused && sceneComponent.BlocksUpdate)
                blockingScenes.Add(sceneEntity);
        }

        return blockingScenes;
    }

    /// <summary>
    ///     Checks if an entity belongs to one of the currently blocking scenes.
    ///     Centralizes scene membership logic to avoid hardcoding component types in systems.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity belongs to a blocking scene, false otherwise.</returns>
    /// <remarks>
    ///     <para>
    ///         This method checks multiple ways an entity can belong to a scene:
    ///         1. Entity IS a blocking scene (e.g., message box windows where window entity = scene entity)
    ///         2. Entity has SceneOwnershipComponent linking it to a blocking scene
    ///         3. Legacy: Entity has MapPopupComponent with SceneEntity property (for backward compatibility)
    ///     </para>
    ///     <para>
    ///         Systems should use this method instead of hardcoding scene membership checks.
    ///     </para>
    /// </remarks>
    public bool DoesEntityBelongToBlockingScene(Entity entity)
    {
        if (!World.IsAlive(entity))
            return false;

        // Get list of blocking scenes once
        var blockingScenes = GetBlockingScenes();
        if (blockingScenes.Count == 0)
            return false; // No blocking scenes, entity doesn't belong to any

        // Check if entity IS a blocking scene (for message boxes, window entity = scene entity)
        foreach (var blockingScene in blockingScenes)
            if (entity.Id == blockingScene.Id)
                return true;

        // Check if entity has SceneOwnershipComponent (preferred, explicit ownership)
        if (World.Has<SceneOwnershipComponent>(entity))
        {
            ref var ownership = ref World.Get<SceneOwnershipComponent>(entity);
            foreach (var blockingScene in blockingScenes)
                if (ownership.SceneEntity.Id == blockingScene.Id)
                    return true;
        }

        // Legacy: Check MapPopupComponent.SceneEntity (for backward compatibility during migration)
        if (World.Has<MapPopupComponent>(entity))
        {
            ref var popupComponent = ref World.Get<MapPopupComponent>(entity);
            foreach (var blockingScene in blockingScenes)
                if (popupComponent.SceneEntity.Id == blockingScene.Id)
                    return true;
        }

        return false;
    }

    /// <summary>
    ///     Renders all scenes in reverse priority order (lowest priority first, highest priority last).
    ///     This ensures higher priority scenes render on top of lower priority scenes.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    public void Render(GameTime gameTime)
    {
        if (_graphicsDevice == null)
        {
            _logger.Warning("SceneSystem.Render called but GraphicsDevice is null");
            return;
        }

        // Update ScreenSize parameter for all active shaders (tile, sprite, and combined layers)
        var viewport = _graphicsDevice.Viewport;
        _shaderManagerSystem?.UpdateAllLayersScreenSize(viewport.Width, viewport.Height);

        // Iterate scenes in reverse order (lowest priority first, highest priority last)
        // This ensures higher priority scenes render on top
        IterateScenesReverse(
            (sceneEntity, sceneComponent) =>
            {
                // Skip inactive scenes
                if (!sceneComponent.IsActive)
                    return true; // Continue iterating

                // Update shader state for this specific scene (critical timing fix)
                // This ensures per-scene shaders are loaded before rendering
                _shaderManagerSystem?.UpdateShaderState(sceneEntity);

                // Find appropriate scene system and render
                var sceneSystem = FindSceneSystem(sceneEntity);
                sceneSystem?.RenderScene(sceneEntity, gameTime);

                // Check BlocksDraw - if scene blocks draw, stop iterating
                // Note: We iterate in reverse (lowest to highest priority), so if a scene blocks draw,
                // it prevents higher priority scenes (that would render on top) from rendering.
                // This allows lower priority scenes to fully occlude higher priority scenes when needed.
                if (sceneComponent.BlocksDraw)
                    return false; // Stop iterating

                return true; // Continue iterating
            }
        );
    }

    /// <summary>
    ///     Gets the background color for the current scene state.
    ///     Determines color based on the highest priority active scene that blocks draw.
    ///     Requires BackgroundColor to be set on SceneComponent.
    /// </summary>
    /// <returns>The background color to use for clearing the screen.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no active scene with BackgroundColor is found.</exception>
    public Color GetBackgroundColor()
    {
        Color? backgroundColor = null;

        // Iterate scenes in reverse order (lowest priority first, highest priority last)
        // Find the first active scene that blocks draw (this is what will be rendered)
        IterateScenesReverse(
            (sceneEntity, sceneComponent) =>
            {
                // Skip inactive scenes
                if (!sceneComponent.IsActive)
                    return true; // Continue iterating

                // Require BackgroundColor to be set
                if (!sceneComponent.BackgroundColor.HasValue)
                {
                    _logger.Warning(
                        "Scene '{SceneId}' (entity {EntityId}) does not have BackgroundColor set. Scene must have BackgroundColor specified.",
                        sceneComponent.SceneId,
                        sceneEntity.Id
                    );
                    return true; // Continue iterating to find a scene with BackgroundColor
                }

                backgroundColor = sceneComponent.BackgroundColor.Value;

                // If scene blocks draw, stop iterating
                if (sceneComponent.BlocksDraw)
                    return false; // Stop iterating

                return true; // Continue iterating
            }
        );

        // Fail fast if no scene with BackgroundColor found
        if (!backgroundColor.HasValue)
            throw new InvalidOperationException(
                "No active scene with BackgroundColor found. All scenes must have BackgroundColor specified."
            );

        return backgroundColor.Value;
    }

    /// <summary>
    ///     Helper method to iterate scenes in priority order with a callback.
    ///     Handles dead entity checks and provides a consistent iteration pattern.
    ///     Used by SceneRendererSystem and SceneInputSystem to avoid code duplication.
    /// </summary>
    /// <param name="processScene">Callback that processes each scene. Return false to stop iteration, true to continue.</param>
    public void IterateScenes(Func<Entity, SceneComponent, bool> processScene)
    {
        foreach (var sceneEntity in _sceneStack)
        {
            if (!World.IsAlive(sceneEntity))
                continue;

            ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity);

            // Process scene - if callback returns false, stop iterating
            if (!processScene(sceneEntity, sceneComponent))
                break;
        }
    }

    /// <summary>
    ///     Helper method to iterate scenes in reverse priority order (lowest to highest) with a callback.
    ///     Used for rendering so that higher priority scenes render last (on top).
    ///     Handles dead entity checks and provides a consistent iteration pattern.
    /// </summary>
    /// <param name="processScene">Callback that processes each scene. Return false to stop iteration, true to continue.</param>
    public void IterateScenesReverse(Func<Entity, SceneComponent, bool> processScene)
    {
        // Iterate in reverse order (lowest priority first, highest priority last)
        for (var i = _sceneStack.Count - 1; i >= 0; i--)
        {
            var sceneEntity = _sceneStack[i];
            if (!World.IsAlive(sceneEntity))
                continue;

            ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity);

            // Process scene - if callback returns false, stop iterating
            if (!processScene(sceneEntity, sceneComponent))
                break;
        }
    }

    /// <summary>
    ///     Removes dead entities from the scene stack and ID lookup.
    ///     Should be called periodically to prevent memory leaks.
    /// </summary>
    private void CleanupDeadEntities()
    {
        // Iterate backwards to safely remove items without allocation
        for (var i = _sceneStack.Count - 1; i >= 0; i--)
        {
            var sceneEntity = _sceneStack[i];
            if (!World.IsAlive(sceneEntity))
            {
                _sceneStack.RemoveAt(i);
                _sceneInsertionOrder.Remove(sceneEntity);

                // Find and remove from ID lookup
                string? sceneIdToRemove = null;
                foreach (var kvp in _sceneIds)
                    if (kvp.Value.Id == sceneEntity.Id)
                    {
                        sceneIdToRemove = kvp.Key;
                        break;
                    }

                if (sceneIdToRemove != null)
                {
                    _sceneIds.Remove(sceneIdToRemove);
                    _logger.Debug(
                        "SceneSystem: Cleaned up dead scene entity '{SceneId}'",
                        sceneIdToRemove
                    );
                }
            }
        }
    }

    /// <summary>
    ///     Sorts the scene stack by priority (highest priority first).
    /// </summary>
    private void SortSceneStack()
    {
        // Clean up dead entities before sorting
        CleanupDeadEntities();

        _sceneStack.Sort(
            (a, b) =>
            {
                if (!World.IsAlive(a) || !World.IsAlive(b))
                    return 0;

                ref var sceneA = ref World.Get<SceneComponent>(a);
                ref var sceneB = ref World.Get<SceneComponent>(b);

                // Sort by priority descending (higher priority first)
                var priorityComparison = sceneB.Priority.CompareTo(sceneA.Priority);
                if (priorityComparison != 0)
                    return priorityComparison;

                // If priorities are equal, maintain insertion order (newer scenes on top)
                // Use insertion order as a stable sort key
                var orderA = _sceneInsertionOrder.TryGetValue(a, out var orderAVal)
                    ? orderAVal
                    : int.MaxValue;
                var orderB = _sceneInsertionOrder.TryGetValue(b, out var orderBVal)
                    ? orderBVal
                    : int.MaxValue;
                return orderB.CompareTo(orderA); // Higher insertion order (newer) first
            }
        );
    }

    /// <summary>
    ///     Cleans up resources when the system is no longer needed.
    /// </summary>
    /// <remarks>
    ///     This method is kept for backward compatibility. Prefer using Dispose().
    /// </remarks>
    public void Cleanup()
    {
        Dispose();
    }

    /// <summary>
    ///     Protected dispose implementation following standard dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            EventBus.Unsubscribe<SceneMessageEvent>(OnSceneMessage);

            _sceneStack.Clear();
            _sceneIds.Clear();
            _sceneInsertionOrder.Clear();
        }

        _disposed = true;
    }
}
