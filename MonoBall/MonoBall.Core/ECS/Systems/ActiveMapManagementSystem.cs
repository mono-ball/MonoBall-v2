using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Services;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that manages the ActiveMapEntity tag component for efficient query-level filtering.
    /// Adds ActiveMapEntity to entities in loaded maps and removes it when maps are unloaded.
    /// This allows other systems to query only entities in active maps at the query level,
    /// avoiding iteration over entities in unloaded maps.
    /// </summary>
    public class ActiveMapManagementSystem
        : BaseSystem<World, float>,
            IPrioritizedSystem,
            IDisposable
    {
        private readonly IActiveMapFilterService _activeMapFilterService;
        private readonly ILogger _logger;
        private readonly QueryDescription _npcQuery;
        private readonly QueryDescription _playerQuery;
        private bool _disposed;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.ActiveMapManagement;

        /// <summary>
        /// Initializes a new instance of the ActiveMapManagementSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="activeMapFilterService">The active map filter service.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ActiveMapManagementSystem(
            World world,
            IActiveMapFilterService activeMapFilterService,
            ILogger logger
        )
            : base(world)
        {
            _activeMapFilterService =
                activeMapFilterService
                ?? throw new ArgumentNullException(nameof(activeMapFilterService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _npcQuery = new QueryDescription().WithAll<NpcComponent>();
            _playerQuery = new QueryDescription().WithAll<PlayerComponent>();

            // Subscribe to map load/unload events
            EventBus.Subscribe<MapLoadedEvent>(OnMapLoaded);
            EventBus.Subscribe<MapUnloadedEvent>(OnMapUnloaded);
        }

        /// <summary>
        /// Updates the system, ensuring ActiveMapEntity tags are correctly applied.
        /// Called every frame to handle entities that may have been created after map load events.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        public override void Update(in float deltaTime)
        {
            HashSet<string> activeMapIds = _activeMapFilterService.GetActiveMapIds();

            // Collect entities that need ActiveMapEntity added/removed (read-only pass)
            // CRITICAL: Cannot modify components during query iteration - causes memory corruption
            var entitiesToAdd = new List<Entity>();
            var entitiesToRemove = new List<Entity>();

            // Update NPCs: Collect entities that need ActiveMapEntity added or removed
            World.Query(
                in _npcQuery,
                (Entity entity, ref NpcComponent npc) =>
                {
                    // Check if entity is still alive before accessing components
                    if (!World.IsAlive(entity))
                        return;

                    bool shouldBeActive = activeMapIds.Contains(npc.MapId);
                    bool isActive = World.Has<ActiveMapEntity>(entity);

                    if (shouldBeActive && !isActive)
                    {
                        entitiesToAdd.Add(entity);
                    }
                    else if (!shouldBeActive && isActive)
                    {
                        entitiesToRemove.Add(entity);
                    }
                }
            );

            // Apply structural changes after query iteration completes
            foreach (var entity in entitiesToAdd)
            {
                if (World.IsAlive(entity) && !World.Has<ActiveMapEntity>(entity))
                {
                    World.Add<ActiveMapEntity>(entity);
                }
            }

            foreach (var entity in entitiesToRemove)
            {
                // Double-check entity is alive and has the component before removing
                // This prevents memory corruption if entity was destroyed between collection and removal
                if (!World.IsAlive(entity))
                {
                    continue; // Entity was destroyed, skip
                }

                if (World.Has<ActiveMapEntity>(entity))
                {
                    try
                    {
                        World.Remove<ActiveMapEntity>(entity);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't crash - entity might have been destroyed concurrently
                        _logger.Warning(
                            ex,
                            "Failed to remove ActiveMapEntity from entity {EntityId}. Entity may have been destroyed.",
                            entity.Id
                        );
                    }
                }
            }

            // Player is always in active maps - collect then add
            var playersToAdd = new List<Entity>();
            World.Query(
                in _playerQuery,
                (Entity entity) =>
                {
                    // Check if entity is still alive before accessing components
                    if (!World.IsAlive(entity))
                        return;

                    if (!World.Has<ActiveMapEntity>(entity))
                    {
                        playersToAdd.Add(entity);
                    }
                }
            );

            // Apply player changes after query iteration completes
            foreach (var entity in playersToAdd)
            {
                if (World.IsAlive(entity) && !World.Has<ActiveMapEntity>(entity))
                {
                    World.Add<ActiveMapEntity>(entity);
                }
            }
        }

        /// <summary>
        /// Event handler for map loaded events.
        /// Invalidates the cache and triggers update to tag entities.
        /// </summary>
        private void OnMapLoaded(ref MapLoadedEvent evt)
        {
            _activeMapFilterService.InvalidateCache();
            _logger.Debug("Map loaded: {MapId}, invalidating active map cache", evt.MapId);
        }

        /// <summary>
        /// Event handler for map unloaded events.
        /// Invalidates the cache and triggers update to remove tags.
        /// </summary>
        private void OnMapUnloaded(ref MapUnloadedEvent evt)
        {
            _activeMapFilterService.InvalidateCache();
            _logger.Debug("Map unloaded: {MapId}, invalidating active map cache", evt.MapId);
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        public new void Dispose() => Dispose(true);

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                EventBus.Unsubscribe<MapLoadedEvent>(OnMapLoaded);
                EventBus.Unsubscribe<MapUnloadedEvent>(OnMapUnloaded);
            }
            _disposed = true;
        }
    }
}
