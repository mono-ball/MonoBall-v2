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
    ///
    /// This system runs with low priority (after most other systems) to reduce race conditions
    /// with entity creation and modification. It processes entities after they have been fully
    /// created and initialized by other systems.
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
                // CRITICAL: Multiple validation checks to prevent memory corruption
                // Entity might be destroyed or in the middle of structural changes between collection and removal

                // Check 1: Entity must still be alive
                if (!World.IsAlive(entity))
                {
                    continue; // Entity was destroyed, skip
                }

                // Check 2: Entity must still have the component
                // This check is critical - if the component was already removed by another system,
                // attempting to remove it again can cause memory corruption
                if (!World.Has<ActiveMapEntity>(entity))
                {
                    continue; // Component already removed, skip
                }

                // Check 3: Verify entity still has NpcComponent (for NPCs) to ensure it's still valid
                // This helps catch cases where the entity's archetype is being modified
                if (!World.Has<NpcComponent>(entity))
                {
                    // Entity lost NpcComponent - might be in the middle of destruction or modification
                    // Skip removal to avoid memory corruption
                    continue;
                }

                // All checks passed - attempt to remove component
                // Note: AccessViolationException typically cannot be caught in managed code,
                // so the defensive checks above are critical to prevent it from occurring
                try
                {
                    // Final validation: ensure entity is still in a valid state
                    // This double-check helps catch race conditions where entity state changes
                    // between the checks above and the actual removal
                    if (
                        !World.IsAlive(entity)
                        || !World.Has<ActiveMapEntity>(entity)
                        || !World.Has<NpcComponent>(entity)
                    )
                    {
                        // Entity state changed between checks - skip removal to avoid corruption
                        continue;
                    }

                    World.Remove<ActiveMapEntity>(entity);
                }
                catch (Exception ex)
                {
                    // Catch any exceptions (though AccessViolationException typically can't be caught)
                    // Log and continue - entity may have been destroyed or modified concurrently
                    _logger.Warning(
                        ex,
                        "Failed to remove ActiveMapEntity from entity {EntityId}. "
                            + "Entity may have been destroyed or modified. Skipping removal.",
                        entity.Id
                    );
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
