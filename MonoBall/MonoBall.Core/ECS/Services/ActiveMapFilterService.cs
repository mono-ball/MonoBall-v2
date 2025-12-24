using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Service that provides filtering and querying for entities in active maps.
    /// Uses cached active map IDs and efficient component lookups.
    /// </summary>
    public class ActiveMapFilterService : IActiveMapFilterService
    {
        private readonly World _world;
        private readonly QueryDescription _mapQuery;
        private readonly QueryDescription _playerQuery;
        private readonly QueryDescription _mapWithPositionQuery;
        private HashSet<string>? _cachedActiveMapIds;
        private int _lastMapEntityCount = -1;

        /// <summary>
        /// Initializes a new instance of the ActiveMapFilterService.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        public ActiveMapFilterService(World world)
        {
            _world = world ?? throw new System.ArgumentNullException(nameof(world));
            _mapQuery = new QueryDescription().WithAll<MapComponent>();
            _playerQuery = new QueryDescription().WithAll<PlayerComponent, PositionComponent>();
            _mapWithPositionQuery = new QueryDescription().WithAll<
                MapComponent,
                PositionComponent
            >();
        }

        /// <summary>
        /// Gets the set of active map IDs (all currently loaded maps).
        /// Uses caching to avoid recalculating every call - cache is invalidated when map entity count changes.
        /// </summary>
        /// <returns>A set of active map IDs.</returns>
        public HashSet<string> GetActiveMapIds()
        {
            // Quick check: if map entity count hasn't changed, return cached result
            int currentCount = _world.CountEntities(in _mapQuery);
            if (_cachedActiveMapIds != null && currentCount == _lastMapEntityCount)
            {
                return _cachedActiveMapIds;
            }

            // Rebuild cache (maps were loaded/unloaded)
            HashSet<string> activeMapIds = new HashSet<string>();

            // Query all map entities to get loaded maps
            _world.Query(
                in _mapQuery,
                (Entity entity, ref MapComponent map) =>
                {
                    activeMapIds.Add(map.MapId);
                }
            );

            // Update cache
            _cachedActiveMapIds = activeMapIds;
            _lastMapEntityCount = currentCount;

            return activeMapIds;
        }

        /// <summary>
        /// Checks if an entity is in one of the active maps.
        /// For NPCs: checks NpcComponent.MapId.
        /// For player: always returns true (player is always in an active map).
        /// For other entities: checks if they have MapComponent or are in any active map.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if the entity is in an active map, false otherwise.</returns>
        public bool IsEntityInActiveMaps(Entity entity)
        {
            HashSet<string> activeMapIds = GetActiveMapIds();

            // Try to get components in order of likelihood to avoid unnecessary lookups
            // Player is always processed (they're always in an active map)
            if (_world.TryGet<PlayerComponent>(entity, out _))
            {
                return true;
            }

            // NPCs: Check NpcComponent.MapId (most common case for entities with GridMovement)
            if (_world.TryGet<NpcComponent>(entity, out var npcComponent))
            {
                return activeMapIds.Contains(npcComponent.MapId);
            }

            // Entities with MapComponent (map entities themselves)
            if (_world.TryGet<MapComponent>(entity, out var mapComponent))
            {
                return activeMapIds.Contains(mapComponent.MapId);
            }

            // For other entities without explicit map association, don't process them
            // (they might be in unloaded maps or have no map context)
            return false;
        }

        /// <summary>
        /// Gets the map ID for an entity if it has one.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>The map ID, or null if the entity doesn't have a map association.</returns>
        public string? GetEntityMapId(Entity entity)
        {
            // Check NPCs first (most common)
            if (_world.TryGet<NpcComponent>(entity, out var npcComponent))
            {
                return npcComponent.MapId;
            }

            // Check MapComponent
            if (_world.TryGet<MapComponent>(entity, out var mapComponent))
            {
                return mapComponent.MapId;
            }

            return null;
        }

        /// <summary>
        /// Gets the map ID that the player is currently positioned in.
        /// </summary>
        /// <returns>The map ID containing the player, or null if player not found or not in any map.</returns>
        public string? GetPlayerCurrentMapId()
        {
            // Query for player entity
            Vector2? playerPixelPos = null;
            _world.Query(
                in _playerQuery,
                (Entity entity, ref PositionComponent position) =>
                {
                    playerPixelPos = new Vector2(position.PixelX, position.PixelY);
                }
            );

            if (!playerPixelPos.HasValue)
            {
                return null;
            }

            // Find which map contains the player
            string? playerMapId = null;
            _world.Query(
                in _mapWithPositionQuery,
                (Entity entity, ref MapComponent map, ref PositionComponent mapPosition) =>
                {
                    // If we already found a map, skip remaining maps (return first match)
                    if (playerMapId != null)
                    {
                        return;
                    }

                    // Calculate map bounds in pixels
                    float mapLeft = mapPosition.Position.X;
                    float mapTop = mapPosition.Position.Y;
                    float mapRight = mapLeft + (map.Width * map.TileWidth);
                    float mapBottom = mapTop + (map.Height * map.TileHeight);

                    // Check if player is within map bounds
                    if (
                        playerPixelPos.Value.X >= mapLeft
                        && playerPixelPos.Value.X < mapRight
                        && playerPixelPos.Value.Y >= mapTop
                        && playerPixelPos.Value.Y < mapBottom
                    )
                    {
                        playerMapId = map.MapId;
                    }
                }
            );

            return playerMapId;
        }

        /// <summary>
        /// Invalidates the cached active map IDs, forcing a recalculation on next access.
        /// Call this when maps are loaded or unloaded.
        /// </summary>
        public void InvalidateCache()
        {
            _cachedActiveMapIds = null;
            _lastMapEntityCount = -1;
        }
    }
}
