using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Events;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System responsible for managing scene lifecycle, priority stack, and state.
    /// Does NOT handle update/render - that's done by scene-specific systems.
    /// </summary>
    public class SceneSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
    {
        private readonly List<Entity> _sceneStack = new List<Entity>();
        private readonly Dictionary<string, Entity> _sceneIds = new Dictionary<string, Entity>();
        private readonly Dictionary<Entity, int> _sceneInsertionOrder =
            new Dictionary<Entity, int>();
        private int _nextInsertionOrder = 0;
        private readonly QueryDescription _cameraQueryDescription;
        private bool _disposed = false;

        private readonly ILogger _logger;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.Scene;

        /// <summary>
        /// Initializes a new instance of the SceneSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public SceneSystem(World world, ILogger logger)
            : base(world)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Subscribe to SceneMessageEvent for inter-scene communication
            EventBus.Subscribe<SceneMessageEvent>(OnSceneMessage);
            _cameraQueryDescription = new QueryDescription().WithAll<CameraComponent>();
        }

        /// <summary>
        /// Creates a scene entity and adds it to the scene stack.
        /// </summary>
        /// <param name="sceneComponent">The scene component data.</param>
        /// <param name="additionalComponents">Additional components to add to the scene entity.</param>
        /// <returns>The created scene entity.</returns>
        public Entity CreateScene(
            SceneComponent sceneComponent,
            params object[] additionalComponents
        )
        {
            if (string.IsNullOrEmpty(sceneComponent.SceneId))
            {
                throw new ArgumentException(
                    "Scene ID cannot be null or empty.",
                    nameof(sceneComponent)
                );
            }

            if (_sceneIds.ContainsKey(sceneComponent.SceneId))
            {
                throw new InvalidOperationException(
                    $"Scene with ID '{sceneComponent.SceneId}' already exists."
                );
            }

            // Fire SceneCreatingEvent to allow systems to prepare or cancel creation
            bool cancelled = SceneEventHelper.FireSceneCreating(sceneComponent.SceneId);
            if (cancelled)
            {
                _logger.Information(
                    "Scene creation cancelled for '{SceneId}'",
                    sceneComponent.SceneId
                );
                throw new InvalidOperationException(
                    $"Scene creation was cancelled for '{sceneComponent.SceneId}'."
                );
            }

            // Validate BackgroundColor is set
            if (!sceneComponent.BackgroundColor.HasValue)
            {
                throw new ArgumentException(
                    "BackgroundColor must be set on SceneComponent. All scenes must specify a background color.",
                    nameof(sceneComponent)
                );
            }

            // Validate CameraEntityId if SceneCamera mode is specified
            if (sceneComponent.CameraMode == SceneCameraMode.SceneCamera)
            {
                if (!sceneComponent.CameraEntityId.HasValue)
                {
                    throw new ArgumentException(
                        "CameraEntityId must be set when CameraMode is SceneCamera.",
                        nameof(sceneComponent)
                    );
                }

                // Verify the camera entity exists and has CameraComponent
                // Query for the camera entity by ID
                var cameraEntityId = sceneComponent.CameraEntityId.Value;
                bool cameraFound = false;

                World.Query(
                    in _cameraQueryDescription,
                    (Entity entity, ref CameraComponent _) =>
                    {
                        if (entity.Id == cameraEntityId)
                        {
                            cameraFound = true;
                        }
                    }
                );

                if (!cameraFound)
                {
                    throw new ArgumentException(
                        $"Camera entity {cameraEntityId} does not exist or does not have CameraComponent.",
                        nameof(sceneComponent)
                    );
                }
            }

            // Create scene entity with SceneComponent
            // Note: Arch ECS handles struct components correctly, but we ensure the component
            // is set properly by using Set() after creation to avoid any potential boxing issues
            Entity sceneEntity = World.Create(sceneComponent);

            // Ensure component is stored correctly (defensive check)
            ref var storedSceneComponent = ref World.Get<SceneComponent>(sceneEntity);
            storedSceneComponent = sceneComponent;

            // Add additional components if provided
            // Note: We handle scene type components specifically
            if (additionalComponents != null && additionalComponents.Length > 0)
            {
                foreach (var component in additionalComponents)
                {
                    if (component is GameSceneComponent gameSceneComp)
                    {
                        World.Add<GameSceneComponent>(sceneEntity, gameSceneComp);
                    }
                    else if (component is DebugBarSceneComponent debugBarSceneComp)
                    {
                        World.Add<DebugBarSceneComponent>(sceneEntity, debugBarSceneComp);
                    }
                    else if (component is MapPopupSceneComponent mapPopupSceneComp)
                    {
                        World.Add<MapPopupSceneComponent>(sceneEntity, mapPopupSceneComp);
                    }
                    else if (component is LoadingSceneComponent loadingSceneComp)
                    {
                        World.Add<LoadingSceneComponent>(sceneEntity, loadingSceneComp);
                    }
                    else if (component is LoadingProgressComponent loadingProgressComp)
                    {
                        World.Add<LoadingProgressComponent>(sceneEntity, loadingProgressComp);
                    }
                    // Add other component types as needed in the future
                }
            }

            // Add to scene stack and ID lookup
            _sceneStack.Add(sceneEntity);
            _sceneIds[sceneComponent.SceneId] = sceneEntity;
            _sceneInsertionOrder[sceneEntity] = _nextInsertionOrder++;

            // Sort stack by priority (highest first)
            SortSceneStack();

            // Get scene type from marker components
            string? sceneType = SceneEventHelper.GetSceneType(World, sceneEntity);

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
        /// Destroys a scene entity and removes it from the scene stack.
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
            string sceneId = sceneComponent.SceneId;

            // Get scene type from marker components
            string? sceneType = SceneEventHelper.GetSceneType(World, sceneEntity);

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
        /// Destroys a scene entity by scene ID.
        /// </summary>
        /// <param name="sceneId">The scene ID.</param>
        public void DestroyScene(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId))
            {
                throw new ArgumentException("Scene ID cannot be null or empty.", nameof(sceneId));
            }

            if (!_sceneIds.TryGetValue(sceneId, out var sceneEntity))
            {
                _logger.Warning("Scene with ID '{SceneId}' not found", sceneId);
                return;
            }

            DestroyScene(sceneEntity);
        }

        /// <summary>
        /// Gets the scene entity by scene ID.
        /// </summary>
        /// <param name="sceneId">The scene ID.</param>
        /// <returns>The scene entity, or null if not found.</returns>
        public Entity? GetSceneEntity(string sceneId)
        {
            if (string.IsNullOrEmpty(sceneId))
            {
                return null;
            }

            _sceneIds.TryGetValue(sceneId, out var sceneEntity);
            return sceneEntity;
        }

        /// <summary>
        /// Gets the scene stack (priority-ordered list of scene entities).
        /// </summary>
        /// <returns>A read-only list of scene entities, ordered by priority (highest first).</returns>
        public IReadOnlyList<Entity> GetSceneStack()
        {
            return _sceneStack.AsReadOnly();
        }

        /// <summary>
        /// Sets whether a scene is active.
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
            bool wasActive = sceneComponent.IsActive;
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
        /// Sets whether a scene is paused.
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
            bool wasPaused = sceneComponent.IsPaused;
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
        /// Sets the priority of a scene and re-sorts the scene stack.
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
            int oldPriority = sceneComponent.Priority;
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
        /// Handles inter-scene messages.
        /// </summary>
        /// <param name="evt">The scene message event.</param>
        private void OnSceneMessage(ref SceneMessageEvent evt)
        {
            // Handle message based on MessageType
            switch (evt.MessageType.ToLowerInvariant())
            {
                case "pause":
                    if (evt.TargetSceneId != null)
                    {
                        SetScenePaused(evt.TargetSceneId, true);
                    }
                    break;

                case "resume":
                    if (evt.TargetSceneId != null)
                    {
                        SetScenePaused(evt.TargetSceneId, false);
                    }
                    break;

                case "destroy":
                    if (evt.TargetSceneId != null)
                    {
                        DestroyScene(evt.TargetSceneId);
                    }
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
        /// Updates the scene system. Only performs cleanup - scene-specific systems handle updates.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Clean up dead entities before processing
            CleanupDeadEntities();
            // Scene-specific systems handle scene updates
        }

        /// <summary>
        /// Helper method to iterate scenes in priority order with a callback.
        /// Handles dead entity checks and provides a consistent iteration pattern.
        /// Used by SceneRendererSystem and SceneInputSystem to avoid code duplication.
        /// </summary>
        /// <param name="processScene">Callback that processes each scene. Return false to stop iteration, true to continue.</param>
        public void IterateScenes(Func<Entity, SceneComponent, bool> processScene)
        {
            foreach (var sceneEntity in _sceneStack)
            {
                if (!World.IsAlive(sceneEntity))
                {
                    continue;
                }

                ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity);

                // Process scene - if callback returns false, stop iterating
                if (!processScene(sceneEntity, sceneComponent))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Helper method to iterate scenes in reverse priority order (lowest to highest) with a callback.
        /// Used for rendering so that higher priority scenes render last (on top).
        /// Handles dead entity checks and provides a consistent iteration pattern.
        /// </summary>
        /// <param name="processScene">Callback that processes each scene. Return false to stop iteration, true to continue.</param>
        public void IterateScenesReverse(Func<Entity, SceneComponent, bool> processScene)
        {
            // Iterate in reverse order (lowest priority first, highest priority last)
            for (int i = _sceneStack.Count - 1; i >= 0; i--)
            {
                var sceneEntity = _sceneStack[i];
                if (!World.IsAlive(sceneEntity))
                {
                    continue;
                }

                ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity);

                // Process scene - if callback returns false, stop iterating
                if (!processScene(sceneEntity, sceneComponent))
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Removes dead entities from the scene stack and ID lookup.
        /// Should be called periodically to prevent memory leaks.
        /// </summary>
        private void CleanupDeadEntities()
        {
            // Iterate backwards to safely remove items without allocation
            for (int i = _sceneStack.Count - 1; i >= 0; i--)
            {
                var sceneEntity = _sceneStack[i];
                if (!World.IsAlive(sceneEntity))
                {
                    _sceneStack.RemoveAt(i);
                    _sceneInsertionOrder.Remove(sceneEntity);

                    // Find and remove from ID lookup
                    string? sceneIdToRemove = null;
                    foreach (var kvp in _sceneIds)
                    {
                        if (kvp.Value.Id == sceneEntity.Id)
                        {
                            sceneIdToRemove = kvp.Key;
                            break;
                        }
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
        /// Sorts the scene stack by priority (highest priority first).
        /// </summary>
        private void SortSceneStack()
        {
            // Clean up dead entities before sorting
            CleanupDeadEntities();

            _sceneStack.Sort(
                (a, b) =>
                {
                    if (!World.IsAlive(a) || !World.IsAlive(b))
                    {
                        return 0;
                    }

                    ref var sceneA = ref World.Get<SceneComponent>(a);
                    ref var sceneB = ref World.Get<SceneComponent>(b);

                    // Sort by priority descending (higher priority first)
                    int priorityComparison = sceneB.Priority.CompareTo(sceneA.Priority);
                    if (priorityComparison != 0)
                    {
                        return priorityComparison;
                    }

                    // If priorities are equal, maintain insertion order (newer scenes on top)
                    // Use insertion order as a stable sort key
                    int orderA = _sceneInsertionOrder.TryGetValue(a, out var orderAVal)
                        ? orderAVal
                        : int.MaxValue;
                    int orderB = _sceneInsertionOrder.TryGetValue(b, out var orderBVal)
                        ? orderBVal
                        : int.MaxValue;
                    return orderB.CompareTo(orderA); // Higher insertion order (newer) first
                }
            );
        }

        /// <summary>
        /// Cleans up resources when the system is no longer needed.
        /// </summary>
        /// <remarks>
        /// This method is kept for backward compatibility. Prefer using Dispose().
        /// </remarks>
        public void Cleanup()
        {
            Dispose();
        }

        /// <summary>
        /// Disposes of the system and unsubscribes from events.
        /// </summary>
        public new void Dispose() => Dispose(true);

        /// <summary>
        /// Protected dispose implementation following standard dispose pattern.
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
}
