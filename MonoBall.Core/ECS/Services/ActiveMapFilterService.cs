using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Relationships;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Relationships;
using Serilog;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service that provides filtering and querying for entities in active maps.
///     Uses cached active map IDs and efficient component lookups.
/// </summary>
public class ActiveMapFilterService : IActiveMapFilterService
{
    private readonly QueryDescription _mapQuery;
    private readonly QueryDescription _mapWithPositionQuery;
    private readonly QueryDescription _playerQuery;
    private readonly World _world;
    private readonly ILogger _logger;
    private HashSet<string>? _cachedActiveMapIds;
    private string? _cachedPlayerMapId;

    /// <summary>
    ///     Initializes a new instance of the ActiveMapFilterService.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public ActiveMapFilterService(World world, ILogger logger)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mapQuery = new QueryDescription().WithAll<MapComponent>();
        _playerQuery = new QueryDescription().WithAll<PlayerComponent, PositionComponent>();
        _mapWithPositionQuery = new QueryDescription().WithAll<MapComponent, PositionComponent>();
        // Note: Connection queries now use relationships, not component queries
    }

    /// <summary>
    ///     Gets the set of active map IDs (player's current map + directly connected maps).
    ///     Uses caching to avoid recalculating every call - cache is invalidated when player changes maps.
    ///     During initialization (before player exists), returns all loaded maps to prevent NPCs from losing ActiveMapEntity.
    /// </summary>
    /// <returns>A set of active map IDs.</returns>
    /// <remarks>
    ///     This method intentionally returns all loaded maps when the player doesn't exist yet (initialization phase).
    ///     This is not fallback code - it's intentional initialization behavior to ensure NPCs remain active during map loading.
    ///     Once the player exists, this method will only return the player's current map and directly connected maps.
    /// </remarks>
    public HashSet<string> GetActiveMapIds()
    {
        // Get player's current map
        var playerMapId = GetPlayerCurrentMapId();

        // Quick check: if player hasn't changed maps, return cached result
        if (_cachedActiveMapIds != null && _cachedPlayerMapId == playerMapId)
            return _cachedActiveMapIds;

        // Rebuild cache (player changed maps or first call)
        var activeMapIds = new HashSet<string>();

        // During initialization: if no player or player not in any map yet,
        // return all loaded maps to prevent NPCs from losing ActiveMapEntity.
        // This is intentional initialization behavior, not fallback code.
        if (string.IsNullOrEmpty(playerMapId))
        {
            _world.Query(
                in _mapQuery,
                (Entity entity, ref MapComponent map) =>
                {
                    activeMapIds.Add(map.MapId);
                }
            );
            _cachedActiveMapIds = activeMapIds;
            _cachedPlayerMapId = playerMapId;
            return activeMapIds;
        }

        // Add player's current map
        activeMapIds.Add(playerMapId);

        // Find map entity for player's current map
        Entity? playerMapEntity = null;
        _world.Query(
            in _mapQuery,
            (Entity entity, ref MapComponent map) =>
            {
                if (map.MapId == playerMapId)
                    playerMapEntity = entity;
            }
        );

        // Query connection entities via relationships to find directly connected maps
        if (playerMapEntity.HasValue && _world.IsAlive(playerMapEntity.Value))
        {
            try
            {
                var connectionRelationships = _world.GetRelationships<OwnsMapConnection>(playerMapEntity.Value);
                if (connectionRelationships != null)
                {
                    foreach (var kvp in connectionRelationships)
                    {
                        var connectionEntity = kvp.Key;
                        if (!_world.IsAlive(connectionEntity))
                            continue;

                        if (!_world.Has<MapConnectionComponent>(connectionEntity))
                            continue;

                        ref var connection = ref _world.Get<MapConnectionComponent>(connectionEntity);
                        activeMapIds.Add(connection.TargetMapId);
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                // Relationship query failed - log and continue without connected maps
                // This is not fatal - player's current map will still be active
                // InvalidOperationException is thrown by Arch.Extended when relationship queries fail
                // (e.g., entity not found, relationship type not registered)
                _logger.Debug(
                    ex,
                    "Failed to query map connections for map {MapId}. Continuing without connected maps.",
                    playerMapId
                );
            }
            catch (ArgumentException ex)
            {
                // Invalid entity or relationship type - log and continue
                // This is not fatal - player's current map will still be active
                _logger.Debug(
                    ex,
                    "Invalid argument in relationship query for map {MapId}. Continuing without connected maps.",
                    playerMapId
                );
            }
        }

        // Update cache
        _cachedActiveMapIds = activeMapIds;
        _cachedPlayerMapId = playerMapId;

        return activeMapIds;
    }

    /// <summary>
    ///     Checks if an entity is in one of the active maps.
    ///     For NPCs: checks NpcComponent.MapId.
    ///     For player: always returns true (player is always in an active map).
    ///     For other entities: checks if they have MapComponent or are in any active map.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity is in an active map, false otherwise.</returns>
    public bool IsEntityInActiveMaps(Entity entity)
    {
        var activeMapIds = GetActiveMapIds();

        // Try to get components in order of likelihood to avoid unnecessary lookups
        // Player is always processed (they're always in an active map)
        if (_world.TryGet<PlayerComponent>(entity, out _))
            return true;

        // NPCs: Check NpcComponent.MapId (most common case for entities with GridMovement)
        if (_world.TryGet<NpcComponent>(entity, out var npcComponent))
            return activeMapIds.Contains(npcComponent.MapId);

        // Entities with MapComponent (map entities themselves)
        if (_world.TryGet<MapComponent>(entity, out var mapComponent))
            return activeMapIds.Contains(mapComponent.MapId);

        // For other entities without explicit map association, don't process them
        // (they might be in unloaded maps or have no map context)
        return false;
    }

    /// <summary>
    ///     Gets the map ID for an entity if it has one.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>The map ID, or null if the entity doesn't have a map association.</returns>
    public string? GetEntityMapId(Entity entity)
    {
        // Check NPCs first (most common)
        if (_world.TryGet<NpcComponent>(entity, out var npcComponent))
            return npcComponent.MapId;

        // Check MapComponent
        if (_world.TryGet<MapComponent>(entity, out var mapComponent))
            return mapComponent.MapId;

        // Check PlayerComponent - player's map is determined by position
        if (_world.TryGet<PlayerComponent>(entity, out _))
            return GetPlayerCurrentMapId();

        return null;
    }

    /// <summary>
    ///     Gets the map ID that the player is currently positioned in.
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
            return null;

        // Find which map contains the player
        string? playerMapId = null;
        _world.Query(
            in _mapWithPositionQuery,
            (Entity entity, ref MapComponent map, ref PositionComponent mapPosition) =>
            {
                // If we already found a map, skip remaining maps (return first match)
                if (playerMapId != null)
                    return;

                // Filter to main map entities only (has Width/Height set)
                // Chunks no longer have MapComponent, but this filter ensures we only process actual map entities
                if (map.Width == 0)
                    return;

                // Calculate map bounds in pixels
                var mapLeft = mapPosition.Position.X;
                var mapTop = mapPosition.Position.Y;
                var mapRight = mapLeft + map.Width * map.TileWidth;
                var mapBottom = mapTop + map.Height * map.TileHeight;

                // Check if player is within map bounds
                if (
                    playerPixelPos.Value.X >= mapLeft
                    && playerPixelPos.Value.X < mapRight
                    && playerPixelPos.Value.Y >= mapTop
                    && playerPixelPos.Value.Y < mapBottom
                )
                    playerMapId = map.MapId;
            }
        );

        return playerMapId;
    }

    /// <summary>
    ///     Invalidates the cached active map IDs, forcing a recalculation on next access.
    ///     Call this when maps are loaded or unloaded, or when player position changes.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedActiveMapIds = null;
        _cachedPlayerMapId = null;
    }
}
