using System;
using Arch.Core;
using Arch.Relationships;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Relationships;
using MonoBall.Core.ECS.Systems;
using Serilog;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for tile-based collision detection.
///     Provides collision queries using tile data, elevation matching, entity collision, and script interactions.
/// </summary>
public sealed class CollisionService : ICollisionService
{
    private readonly ICollisionLayerCache _collisionLayerCache;
    private readonly IEntityElevationService _entityElevationService;
    private readonly IEntityPositionService _entityPositionService;
    private readonly IEntityQueryService _entityQueryService;
    private readonly ILogger _logger;
    private readonly ITileInteractionCache _tileInteractionCache;
    private readonly ITileInteractionDispatcher _tileInteractionDispatcher;
    private readonly World _world;
    private readonly MapLoaderSystem? _mapLoaderSystem;

    // Cached query descriptions
    private static readonly QueryDescription MapWithPositionQuery = new QueryDescription().WithAll<
        MapComponent,
        PositionComponent
    >();

    /// <summary>
    ///     Initializes a new instance of CollisionService.
    /// </summary>
    /// <param name="mapLoaderSystem">Optional map loader system for loading maps on-demand during transitions.</param>
    public CollisionService(
        World world,
        ICollisionLayerCache collisionLayerCache,
        IEntityElevationService entityElevationService,
        IEntityPositionService entityPositionService,
        IEntityQueryService entityQueryService,
        ITileInteractionCache tileInteractionCache,
        ITileInteractionDispatcher tileInteractionDispatcher,
        ILogger logger,
        MapLoaderSystem? mapLoaderSystem = null
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _collisionLayerCache =
            collisionLayerCache ?? throw new ArgumentNullException(nameof(collisionLayerCache));
        _entityElevationService =
            entityElevationService
            ?? throw new ArgumentNullException(nameof(entityElevationService));
        _entityPositionService =
            entityPositionService ?? throw new ArgumentNullException(nameof(entityPositionService));
        _entityQueryService =
            entityQueryService ?? throw new ArgumentNullException(nameof(entityQueryService));
        _tileInteractionCache =
            tileInteractionCache ?? throw new ArgumentNullException(nameof(tileInteractionCache));
        _tileInteractionDispatcher =
            tileInteractionDispatcher
            ?? throw new ArgumentNullException(nameof(tileInteractionDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mapLoaderSystem = mapLoaderSystem;
    }

    /// <inheritdoc />
    public bool CanMoveTo(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection = Direction.None
    )
    {
        return CanMoveToInternal(entity, targetX, targetY, mapId, fromDirection, fireEvents: true);
    }

    /// <inheritdoc />
    public bool CanMoveToSilent(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection = Direction.None
    )
    {
        return CanMoveToInternal(entity, targetX, targetY, mapId, fromDirection, fireEvents: false);
    }

    /// <inheritdoc />
    public (bool isJumpTile, Direction allowedJumpDir, bool isWalkable) GetTileCollisionInfo(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection
    )
    {
        if (mapId == null)
            return (false, Direction.None, false);

        // Get entity elevation (fail-fast if missing - consistent with CanMoveToInternal)
        if (!_entityElevationService.TryGetEntityElevation(entity, out var entityElevation))
        {
            throw new InvalidOperationException(
                $"Entity {entity.Id} does not have ElevationComponent. "
                    + "All movable entities must have ElevationComponent."
            );
        }

        // Check if tile has jump behavior via interaction cache
        var interactionId = _tileInteractionCache.GetTileInteractionId(
            mapId,
            targetX,
            targetY,
            entityElevation
        );

        // TODO: When jump scripts are implemented, check if this is a jump tile
        // For now, assume no jump tiles
        var isJumpTile = false;
        var allowedJumpDir = Direction.None;

        // Check walkability
        var isWalkable = CanMoveToInternal(
            entity,
            targetX,
            targetY,
            mapId,
            fromDirection,
            fireEvents: false
        );

        return (isJumpTile, allowedJumpDir, isWalkable);
    }

    /// <inheritdoc />
    public MovementResolution ResolveMovement(
        Entity entity,
        int targetX,
        int targetY,
        string? sourceMapId,
        Direction fromDirection = Direction.None
    )
    {
        if (sourceMapId == null)
        {
            _logger.Debug("[Collision] ResolveMovement: sourceMapId is null, blocking movement");
            return MovementResolution.Blocked;
        }

        // Get entity's current pixel position and tile dimensions for pixel calculation
        var (currentPixelX, currentPixelY, tileWidth, tileHeight) = GetEntityPositionInfo(
            entity,
            sourceMapId
        );

        // Calculate target pixel position based on movement direction
        // This is simpler and more reliable than using map world positions
        var (deltaX, deltaY) = fromDirection.ToTileDelta();
        var targetPixelX = currentPixelX + deltaX * tileWidth;
        var targetPixelY = currentPixelY + deltaY * tileHeight;

        // Check if target is in bounds on source map
        if (IsTileInBounds(sourceMapId, targetX, targetY))
        {
            // Same-map movement - check collision normally
            var canMove = CanMoveToInternal(
                entity,
                targetX,
                targetY,
                sourceMapId,
                fromDirection,
                fireEvents: true
            );

            if (!canMove)
                return MovementResolution.Blocked;

            return MovementResolution.Success(
                sourceMapId,
                targetX,
                targetY,
                targetPixelX,
                targetPixelY
            );
        }

        // Out of bounds - try to find a connected map
        var (crossMapId, crossX, crossY) = FindCrossMapPosition(sourceMapId, targetX, targetY);

        if (crossMapId == null)
        {
            _logger.Debug(
                "[Collision] ResolveMovement: Position ({X}, {Y}) is out of bounds for map {MapId} and no connected map found",
                targetX,
                targetY,
                sourceMapId
            );
            return MovementResolution.Blocked;
        }

        _logger.Debug(
            "[Collision] ResolveMovement: Cross-map movement from {SourceMapId} ({SourceX}, {SourceY}) to {TargetMapId} ({TargetX}, {TargetY})",
            sourceMapId,
            targetX,
            targetY,
            crossMapId,
            crossX,
            crossY
        );

        // Check collision on the connected map
        var canMoveCross = CanMoveToInternal(
            entity,
            crossX,
            crossY,
            crossMapId,
            fromDirection,
            fireEvents: true
        );

        if (!canMoveCross)
            return MovementResolution.Blocked;

        // For cross-map movement, pixel position is still just one tile in the movement direction
        return MovementResolution.CrossMap(crossMapId, crossX, crossY, targetPixelX, targetPixelY);
    }

    /// <summary>
    ///     Gets the entity's current pixel position and tile dimensions.
    /// </summary>
    private (float pixelX, float pixelY, int tileWidth, int tileHeight) GetEntityPositionInfo(
        Entity entity,
        string mapId
    )
    {
        float pixelX = 0;
        float pixelY = 0;

        // Get entity's pixel position
        if (_world.TryGet<PositionComponent>(entity, out var position))
        {
            pixelX = position.PixelX;
            pixelY = position.PixelY;
        }

        // Get tile dimensions from main map entity (has Width > 0)
        int tileWidth = 16;
        int tileHeight = 16;

        _world.Query(
            in MapWithPositionQuery,
            (Entity mapEntity, ref MapComponent map, ref PositionComponent pos) =>
            {
                // Filter to main map entity only (has Width/Height set)
                // Chunks no longer have MapComponent, but this filter ensures we only process actual map entities
                if (map.MapId == mapId && map.Width > 0)
                {
                    tileWidth = map.TileWidth;
                    tileHeight = map.TileHeight;
                }
            }
        );

        return (pixelX, pixelY, tileWidth, tileHeight);
    }

    /// <summary>
    ///     Internal collision check implementation shared by CanMoveTo and CanMoveToSilent.
    /// </summary>
    private bool CanMoveToInternal(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection,
        bool fireEvents
    )
    {
        if (mapId == null)
        {
            _logger.Debug("[Collision] mapId is null, blocking movement");
            return false;
        }

        // Bounds check - if out of bounds, try to find a connected map at that position
        if (!IsTileInBounds(mapId, targetX, targetY))
        {
            // Try to find a connected map that contains this position
            var (crossMapId, crossX, crossY) = FindCrossMapPosition(mapId, targetX, targetY);
            if (crossMapId != null)
            {
                _logger.Debug(
                    "[Collision] Position ({X}, {Y}) on map {MapId} is in connected map {CrossMapId} at ({CrossX}, {CrossY})",
                    targetX,
                    targetY,
                    mapId,
                    crossMapId,
                    crossX,
                    crossY
                );
                // Recursively check collision on the connected map
                return CanMoveToInternal(
                    entity,
                    crossX,
                    crossY,
                    crossMapId,
                    fromDirection,
                    fireEvents
                );
            }

            _logger.Debug(
                "[Collision] Position ({X}, {Y}) is out of bounds for map {MapId} and no connected map found",
                targetX,
                targetY,
                mapId
            );
            return false;
        }

        // Get entity elevation
        if (!_entityElevationService.TryGetEntityElevation(entity, out var entityElevation))
        {
            throw new InvalidOperationException(
                $"Entity {entity.Id} does not have ElevationComponent. "
                    + "All movable entities must have ElevationComponent."
            );
        }

        // Check tile interaction scripts (if fireEvents is true)
        if (
            fireEvents
            && IsTileInteractionBlocking(
                entity,
                mapId,
                targetX,
                targetY,
                entityElevation,
                fromDirection
            )
        )
        {
            _logger.Debug(
                "[Collision] Tile interaction blocking at ({X}, {Y}) elevation {Elevation}",
                targetX,
                targetY,
                entityElevation
            );
            return false;
        }

        // Check tile collision
        if (IsTileBlocking(mapId, targetX, targetY, entityElevation))
        {
            _logger.Debug(
                "[Collision] Tile blocking at ({X}, {Y}) elevation {Elevation} on map {MapId}",
                targetX,
                targetY,
                entityElevation,
                mapId
            );
            return false;
        }

        // Check entity collision
        if (IsEntityAtPositionBlocking(mapId, targetX, targetY, entityElevation, entity))
        {
            _logger.Debug(
                "[Collision] Entity blocking at ({X}, {Y}) elevation {Elevation}",
                targetX,
                targetY,
                entityElevation
            );
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Checks if the tile is within map bounds.
    /// </summary>
    private bool IsTileInBounds(string mapId, int x, int y)
    {
        return _collisionLayerCache.IsInBounds(mapId, x, y);
    }

    /// <summary>
    ///     Checks if a tile interaction script blocks movement.
    /// </summary>
    private bool IsTileInteractionBlocking(
        Entity entity,
        string mapId,
        int targetX,
        int targetY,
        byte entityElevation,
        Direction fromDirection
    )
    {
        var interactionId = _tileInteractionCache.GetTileInteractionId(
            mapId,
            targetX,
            targetY,
            entityElevation
        );

        if (interactionId == null)
            return false;

        // Get entity's current position
        var currentPosition = _entityQueryService.GetEntityPosition(entity);

        return _tileInteractionDispatcher.CheckCollision(
            interactionId,
            entity,
            currentPosition,
            (targetX, targetY),
            fromDirection,
            entityElevation
        );
    }

    /// <summary>
    ///     Checks if a tile blocks movement based on collision value at the entity's elevation.
    ///     Uses elevation fallback: if entity's elevation layer is passable, also check elevation 0
    ///     as a base layer (common Pokemon pattern where ground-level collision applies to all).
    /// </summary>
    private bool IsTileBlocking(string mapId, int x, int y, byte entityElevation)
    {
        // Get collision value for this tile at the entity's elevation layer
        var collision = _collisionLayerCache.GetCollisionValue(mapId, x, y, entityElevation);

        // DEBUG: Log collision lookup results
        _logger.Verbose(
            "[Collision] GetCollisionValue({MapId}, {X}, {Y}, elevation={Elevation}) = {Value}",
            mapId,
            x,
            y,
            entityElevation,
            collision?.ToString() ?? "null"
        );

        // If out of bounds, returns null - treat as blocking
        if (!collision.HasValue)
        {
            _logger.Debug(
                "[Collision] No collision data for map {MapId} at ({X}, {Y}) elevation {Elevation} - treating as blocking",
                mapId,
                x,
                y,
                entityElevation
            );
            return true;
        }

        // If blocked at entity's elevation, return true immediately
        if (collision.Value > 0)
            return true;

        // Elevation fallback: if entity is not at ground level, also check elevation 0 (base layer)
        // This implements the Pokemon pattern where ground-level collision applies to all elevations
        if (entityElevation != 0)
        {
            var baseCollision = _collisionLayerCache.GetCollisionValue(mapId, x, y, 0);

            _logger.Verbose(
                "[Collision] Fallback check at elevation 0: GetCollisionValue({MapId}, {X}, {Y}, elevation=0) = {Value}",
                mapId,
                x,
                y,
                baseCollision?.ToString() ?? "null"
            );

            // If base layer blocks, treat as blocking
            if (baseCollision.HasValue && baseCollision.Value > 0)
                return true;
        }

        // Passable at both entity elevation and base elevation
        return false;
    }

    /// <summary>
    ///     Checks if any entity at the target position blocks movement.
    /// </summary>
    private bool IsEntityAtPositionBlocking(
        string mapId,
        int x,
        int y,
        byte entityElevation,
        Entity movingEntity
    )
    {
        // Convert local tile coordinates to global tile coordinates
        // Spatial hash stores entities at global positions, but collision queries use local positions
        var (globalX, globalY) = LocalToGlobalTilePosition(mapId, x, y);

        // Get all entities at this position and elevation
        var entitiesAtPosition = _entityPositionService.GetEntitiesAt(
            mapId,
            globalX,
            globalY,
            entityElevation
        );

        foreach (var otherEntity in entitiesAtPosition)
        {
            // Skip self
            if (otherEntity.Id == movingEntity.Id)
                continue;

            // Skip destroyed entities
            if (!_entityQueryService.IsEntityAlive(otherEntity))
                continue;

            if (IsEntityBlocking(otherEntity))
                return true;
        }

        // Also check entities at elevation 0 (wildcard)
        if (entityElevation != 0)
        {
            var wildcardEntities = _entityPositionService.GetEntitiesAt(mapId, globalX, globalY, 0);
            foreach (var otherEntity in wildcardEntities)
            {
                if (otherEntity.Id == movingEntity.Id)
                    continue;

                // Skip destroyed entities
                if (!_entityQueryService.IsEntityAlive(otherEntity))
                    continue;

                if (IsEntityBlocking(otherEntity))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Checks if a specific entity blocks movement.
    /// </summary>
    private bool IsEntityBlocking(Entity entity)
    {
        // Invisible entities don't block (e.g., NPC with visibility flag evaluating to false)
        if (_world.TryGet<RenderableComponent>(entity, out var renderable) && !renderable.IsVisible)
            return false;

        // Only entities with CollisionComponent can block
        if (!_entityQueryService.TryGetCollisionComponent(entity, out var collision))
            return false;

        // PassThrough allows movement regardless of IsSolid
        if (collision.AllowPassThrough)
            return false;

        return collision.IsSolid;
    }

    /// <summary>
    ///     Converts local tile coordinates to global tile coordinates for a given map.
    ///     Used to query the spatial hash which stores entities at global positions.
    /// </summary>
    /// <param name="mapId">The map ID.</param>
    /// <param name="localX">The local tile X coordinate.</param>
    /// <param name="localY">The local tile Y coordinate.</param>
    /// <returns>The global tile coordinates (X, Y).</returns>
    private (int globalX, int globalY) LocalToGlobalTilePosition(
        string mapId,
        int localX,
        int localY
    )
    {
        int mapTileOffsetX = 0;
        int mapTileOffsetY = 0;
        bool foundMainMapEntity = false;

        _world.Query(
            in MapWithPositionQuery,
            (Entity entity, ref MapComponent map, ref PositionComponent pos) =>
            {
                // Skip if we already found the main map entity
                if (foundMainMapEntity)
                    return;

                // Filter to main map entity only (has Width/Height set)
                // Chunks no longer have MapComponent, but this filter ensures we only process actual map entities
                if (map.MapId == mapId && map.Width > 0)
                {
                    // Calculate tile offset from pixel position
                    // Map entity stores position in pixels, so divide by tile dimensions
                    mapTileOffsetX = (int)(pos.Position.X / map.TileWidth);
                    mapTileOffsetY = (int)(pos.Position.Y / map.TileHeight);
                    foundMainMapEntity = true;
                }
            }
        );

        return (mapTileOffsetX + localX, mapTileOffsetY + localY);
    }

    /// <summary>
    ///     Finds the connected map and grid position for an out-of-bounds position.
    ///     Uses relationships to find connection entities linked to the source map.
    /// </summary>
    /// <param name="sourceMapId">The source map ID.</param>
    /// <param name="targetX">The target X position (may be out of bounds).</param>
    /// <param name="targetY">The target Y position (may be out of bounds).</param>
    /// <returns>
    ///     Tuple of (targetMapId, gridX, gridY) if a connected map exists,
    ///     or (null, 0, 0) if no connection found.
    /// </returns>
    private (string? mapId, int x, int y) FindCrossMapPosition(
        string sourceMapId,
        int targetX,
        int targetY
    )
    {
        // Query 1: Get source map entity and dimensions
        Entity? sourceMapEntity = null;
        MapComponent? sourceMap = null;
        _world.Query(
            in MapWithPositionQuery,
            (Entity entity, ref MapComponent map, ref PositionComponent pos) =>
            {
                // Filter to main map entity only (has Width/Height set)
                // Tile chunk entities also have MapComponent but with Width=0
                if (map.MapId == sourceMapId && map.Width > 0)
                {
                    sourceMapEntity = entity;
                    sourceMap = map;
                }
            }
        );

        if (!sourceMapEntity.HasValue || !sourceMap.HasValue)
        {
            _logger.Debug(
                "[Collision] FindCrossMapPosition: Source map {MapId} not found",
                sourceMapId
            );
            return (null, 0, 0);
        }

        // Determine crossing direction based on source map dimensions
        MapConnectionDirection? crossingDir = null;
        if (targetX < 0)
            crossingDir = MapConnectionDirection.West;
        else if (targetX >= sourceMap.Value.Width)
            crossingDir = MapConnectionDirection.East;
        else if (targetY < 0)
            crossingDir = MapConnectionDirection.North;
        else if (targetY >= sourceMap.Value.Height)
            crossingDir = MapConnectionDirection.South;

        if (!crossingDir.HasValue)
        {
            _logger.Debug(
                "[Collision] FindCrossMapPosition: Position ({X}, {Y}) is within bounds for map {MapId} ({Width}x{Height})",
                targetX,
                targetY,
                sourceMapId,
                sourceMap.Value.Width,
                sourceMap.Value.Height
            );
            return (null, 0, 0);
        }

        // Query 2: Find connection for this direction using relationships
        // Connection entities are separate entities linked via OwnsMapConnection relationship
        string? targetMapId = null;
        int connectionOffset = 0;
        try
        {
            var connectionRelationships = _world.GetRelationships<OwnsMapConnection>(sourceMapEntity.Value);
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
                    if (connection.Direction == crossingDir.Value)
                    {
                        targetMapId = connection.TargetMapId;
                        connectionOffset = connection.Offset;
                        break;
                    }
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // Relationship query failed - log and continue
            // InvalidOperationException is thrown by Arch.Extended when relationship queries fail
            _logger.Debug(
                ex,
                "[Collision] FindCrossMapPosition: Failed to query connections for map {MapId}",
                sourceMapId
            );
        }
        catch (ArgumentException ex)
        {
            // Invalid entity or relationship type
            _logger.Debug(
                ex,
                "[Collision] FindCrossMapPosition: Invalid argument in relationship query for map {MapId}",
                sourceMapId
            );
        }

        if (targetMapId == null)
        {
            _logger.Debug(
                "[Collision] FindCrossMapPosition: No {Direction} connection found for map {MapId}",
                crossingDir.Value,
                sourceMapId
            );
            return (null, 0, 0);
        }

        // Second query to get target map dimensions from main map entity (has Width > 0)
        MapComponent? targetMap = null;
        _world.Query(
            in MapWithPositionQuery,
            (Entity entity, ref MapComponent map, ref PositionComponent pos) =>
            {
                // Filter to main map entity only (has Width/Height set)
                // Tile chunk entities also have MapComponent but with Width=0
                if (map.MapId == targetMapId && map.Width > 0)
                {
                    targetMap = map;
                }
            }
        );

        if (!targetMap.HasValue)
        {
            // Target map is not loaded - try to load it if MapLoaderSystem is available
            if (_mapLoaderSystem != null && !string.IsNullOrEmpty(targetMapId))
            {
                _logger.Information(
                    "[Collision] FindCrossMapPosition: Target map {MapId} not loaded, attempting to load it",
                    targetMapId
                );

                // Try to load the map (will calculate position from connection if possible)
                // Note: This is a synchronous load, which may cause a brief pause
                _mapLoaderSystem.LoadMap(targetMapId);

                // Query again to see if the map is now loaded
                _world.Query(
                    in MapWithPositionQuery,
                    (Entity entity, ref MapComponent map, ref PositionComponent pos) =>
                    {
                        if (map.MapId == targetMapId && map.Width > 0)
                        {
                            targetMap = map;
                        }
                    }
                );
            }

            if (!targetMap.HasValue)
            {
                _logger.Debug(
                    "[Collision] FindCrossMapPosition: Target map {MapId} not loaded",
                    targetMapId
                );
                return (null, 0, 0);
            }
        }

        var tgtWidth = targetMap.Value.Width;
        var tgtHeight = targetMap.Value.Height;

        // Calculate the grid position on the target map
        // The offset adjusts the perpendicular coordinate
        int resultX,
            resultY;

        switch (crossingDir.Value)
        {
            case MapConnectionDirection.East:
                // Entering target map from its west side
                resultX = 0;
                resultY = targetY - connectionOffset;
                break;

            case MapConnectionDirection.West:
                // Entering target map from its east side
                resultX = tgtWidth - 1;
                resultY = targetY - connectionOffset;
                break;

            case MapConnectionDirection.South:
                // Entering target map from its north side
                resultX = targetX - connectionOffset;
                resultY = 0;
                break;

            case MapConnectionDirection.North:
                // Entering target map from its south side
                resultX = targetX - connectionOffset;
                resultY = tgtHeight - 1;
                break;

            default:
                return (null, 0, 0);
        }

        _logger.Debug(
            "[Collision] FindCrossMapPosition: Crossing {Direction} from {SourceMapId} ({SrcX}, {SrcY}) to {TargetMapId} ({TgtX}, {TgtY}) offset={Offset}",
            crossingDir.Value,
            sourceMapId,
            targetX,
            targetY,
            targetMapId,
            resultX,
            resultY,
            connectionOffset
        );

        return (targetMapId, resultX, resultY);
    }
}
