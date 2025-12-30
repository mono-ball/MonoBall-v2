# Collision System Design Document

## Overview

This document outlines the design for a comprehensive collision detection system for MonoBall, supporting tile-based movement, elevation layers, directional collision (one-way tiles, jump tiles), and entity-to-entity collision detection.

## Current State

### Existing Infrastructure

1. **`ICollisionService` Interface** (`MonoBall.Core/ECS/Services/ICollisionService.cs`)
   - `CanMoveTo()` - Checks if a tile position is walkable (fires events)
   - `CanMoveToSilent()` - Checks walkability without firing events (for pathfinding)
   - `GetTileCollisionInfo()` - Returns comprehensive collision info (jump tiles, walkability)

2. **`NullCollisionService`** (`MonoBall.Core/ECS/Services/NullCollisionService.cs`)
   - Stub implementation that allows all movement
   - Used during development when collision checking is disabled

3. **MovementSystem Integration**
   - Calls `ICollisionService.CanMoveTo()` before starting movement
   - Publishes `MovementBlockedEvent` when collision detected

4. **Map Data**
   - Collision and elevation stored per-tile in map.bin (from Porycon3 conversion)
   - Collision values: 0 (passable), 1-3 (blocked - all treated the same)
   - Elevation system: 0-15
     - 0 (entity) = wildcard (matches any tile elevation) - allows walking at ground level
     - 0 (tile) = ground level (matches any entity elevation) - allows walking on ground from any elevation
     - 1-15 = normal elevations (must match exactly for solid tiles)
     - **Natural elevation changes**: Entities can change elevation by moving to tiles with different elevations
       - Stairs: Entity moves from elevation 3 to elevation 4 → entity elevation updates to 4
       - Bridges: Entity at elevation 3 can walk under bridge (elevation 15) if collision override = 0
       - Bridges: Entity at elevation 15 can walk on bridge (elevation 15) if collision override = 0
     - Elevation mismatch only blocks if collision override > 0 (solid tile)
     - Passable tiles (collision override = 0) allow movement regardless of elevation mismatch

## Requirements

### Functional Requirements

1. **Tile-Based Collision**
   - Check collision at target tile position before movement
   - Support collision values 0-3 (0=passable, 1-3=blocked)
   - Handle out-of-bounds positions (return blocked)
   - Provide silent collision queries for pathfinding (no events fired)

2. **Elevation Support**
   - Collision and elevation stored per-tile (not per-elevation layer)
   - Elevation mismatch checked AFTER collision override
   - Elevation matching rules:
     - Elevation 0 (entity) = wildcard (matches any tile elevation) - allows walking at ground level
     - Elevation 0 (tile) = ground level (matches any entity elevation) - allows walking on ground from any elevation
     - Elevation 1-15 = must match exactly for collision, but elevation changes are supported
   - **Natural Elevation Changes**: Entities can change elevation by moving to tiles with different elevations
     - Stairs: Entity moves from elevation 3 to elevation 4 tile → entity elevation updates to 4
     - Bridges: Entity at elevation 3 can walk under bridge (elevation 15) if collision override = 0
     - Bridges: Entity at elevation 15 can walk on bridge (elevation 15) if collision override = 0
   - Entity elevation:
     - Preferred: `ElevationComponent` (consistent storage, can be updated during movement)
     - Fallback: Player elevation from constants, NPCs from `NpcComponent.Elevation`
   - Tiles have elevation stored per-tile in map.bin (bits 12-15)

3. **Directional Collision**
   - Handled by tileset tile's `interactionId` property
   - `interactionId` comes from metatile behavior value (e.g., `base:interaction/tiles/ledge_south`, `base:interaction/tiles/deep_water`)
   - One-way tiles: `impassable_south`, `impassable_north`, `impassable_east`, `impassable_west`
   - Jump tiles: `ledge_south`, `ledge_north`, `ledge_east`, `ledge_west` (and diagonal variants)
   - Surf tiles: `deep_water`, `shore_water`, `ocean_water`, `pond_water`
   - Other behaviors: `walk_south`, `slide_east`, `cycling_road_pull_south`, etc.
   - Interaction scripts define collision rules based on `fromDirection`

4. **Entity-to-Entity Collision**
   - Detect when entities occupy same tile
   - NPCs block player movement (unless pass-through flag)
   - Items don't block movement (but can be picked up)
   - Pushable objects can be moved

5. **Event-Driven Mod Integration**
   - Fire collision check events for mod injection
   - Allow scripts to override collision behavior
   - Support custom collision types (water, grass, etc.)

### Performance Requirements

1. **Zero-Allocation Queries**
   - Use spatial hash for O(1) tile lookups
   - Cache collision data per elevation
   - Avoid ECS queries in hot path

2. **Efficient Entity Queries**
   - Spatial hash for entity positions
   - Only check entities at same elevation
   - Batch queries when possible

3. **Minimal Per-Frame Updates**
   - Static tiles indexed once at map load
   - Dynamic entities re-indexed each frame
   - Cache collision layer data

## Architecture

### System Components

```
┌─────────────────────────────────────────────────────────────┐
│                    CollisionService                          │
│  (Implements ICollisionService)                            │
├─────────────────────────────────────────────────────────────┤
│  Dependencies:                                              │
│  • ICollisionLayerCache (per map, per tile)                 │
│  • IEntityPositionService (entity position queries)          │
│  • IEntityElevationService (entity elevation queries)       │
│  • IConstantsService (player elevation constant)            │
│  • EventBus (mod integration, optional for silent queries)   │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              ICollisionService Interface                     │
├─────────────────────────────────────────────────────────────┤
│  • CanMoveTo() - Full collision check with events            │
│  • CanMoveToSilent() - Collision check without events       │
│  • GetTileCollisionInfo() - Detailed collision info         │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              Collision Detection Pipeline                    │
├─────────────────────────────────────────────────────────────┤
│  1. Event System (mod injection - can override everything)  │
│  2. Bounds Check (out of map = blocked)                     │
│  3. Collision Check Event (tile interaction scripts control movement)│
│  4. Tile Collision Override (bits 10-11 from map.bin, 0-3) │
│  5. Elevation Mismatch Check (with special cases for 0 and 15)│
│  6. Entity Collision Check (spatial hash O(1) lookup)       │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              SpatialHashSystem                              │
│  (Implements IEntityPositionService)                        │
├─────────────────────────────────────────────────────────────┤
│  • Re-indexes entities each frame                           │
│  • Map ID → (X, Y, Elevation) → List<Entity>                │
│  • O(1) position lookups                                    │
│  • Range queries for interaction system                     │
└─────────────────────────────────────────────────────────────┘
```

### Data Structures

#### ICollisionLayerCache

```csharp
namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Caches collision and elevation data per map and tile.
    /// Provides fast O(1) lookup of collision values (0-3) and elevation (0-15).
    /// Collision and elevation are stored per-tile (not per-elevation layer).
    /// </summary>
    public interface ICollisionLayerCache
    {
        /// <summary>
        /// Gets the collision override value (0-3) for a tile at the given position.
        /// This is bits 10-11 from map.bin entry.
        /// </summary>
        /// <param name="mapId">The map identifier. Must not be null.</param>
        /// <param name="x">The X coordinate in tile space.</param>
        /// <param name="y">The Y coordinate in tile space.</param>
        /// <returns>
        /// - null: Position is out of bounds
        /// - 0: Passable (no collision override)
        /// - 1-3: Blocked (collision override - all values treated the same)
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapId"/> is null.</exception>
        byte? GetCollisionValue(string mapId, int x, int y);
        
        /// <summary>
        /// Gets the elevation value (0-15) for a tile at the given position.
        /// This is bits 12-15 from map.bin entry.
        /// </summary>
        /// <param name="mapId">The map identifier. Must not be null.</param>
        /// <param name="x">The X coordinate in tile space.</param>
        /// <param name="y">The Y coordinate in tile space.</param>
        /// <returns>
        /// - null: Position is out of bounds
        /// - 0-15: Elevation value
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapId"/> is null.</exception>
        byte? GetElevation(string mapId, int x, int y);
        
        /// <summary>
        /// Gets both collision value and elevation in a single call.
        /// More efficient than separate calls for hot path collision checks.
        /// </summary>
        /// <param name="mapId">The map identifier. Must not be null.</param>
        /// <param name="x">The X coordinate in tile space.</param>
        /// <param name="y">The Y coordinate in tile space.</param>
        /// <returns>
        /// Tuple containing:
        /// - CollisionValue: null if out of bounds, 0-3 otherwise
        /// - Elevation: null if out of bounds, 0-15 otherwise
        /// </returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapId"/> is null.</exception>
        (CollisionValue: byte?, Elevation: byte?) GetTileData(string mapId, int x, int y);
        
        /// <summary>
        /// Checks if an entity elevation matches a tile elevation for collision purposes.
        /// Handles special cases:
        /// - Entity elevation 0 = wildcard (matches any tile elevation) - allows walking at ground level
        /// - Tile elevation 0 = ground level (matches any entity elevation) - allows walking on ground from any elevation
        /// - Otherwise, must match exactly for collision
        /// </summary>
        /// <remarks>
        /// Elevation matching is checked AFTER collision override. If collision override = 0 (passable),
        /// elevation mismatch doesn't block movement (allows walking under bridges, etc.).
        /// 
        /// Natural elevation changes: When an entity moves to a tile with a different elevation and
        /// collision override = 0, the entity's elevation should be updated to match the tile elevation.
        /// This allows natural elevation changes via stairs, ramps, etc.
        /// </remarks>
        /// <param name="entityElevation">The entity's elevation (0-15).</param>
        /// <param name="tileElevation">The tile's elevation (0-15).</param>
        /// <returns>True if elevations match (considering special cases), false otherwise.</returns>
        bool IsElevationMatch(byte entityElevation, byte tileElevation);
        
        /// <summary>
        /// Checks if a position is within map bounds.
        /// </summary>
        /// <param name="mapId">The map identifier. Must not be null.</param>
        /// <param name="x">The X coordinate in tile space.</param>
        /// <param name="y">The Y coordinate in tile space.</param>
        /// <returns>True if position is within bounds, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapId"/> is null.</exception>
        bool IsInBounds(string mapId, int x, int y);
        
        /// <summary>
        /// Loads collision and elevation data from map.bin.
        /// Stores per-tile collision (bits 10-11) and elevation (bits 12-15).
        /// </summary>
        /// <param name="mapId">The map identifier. Must not be null.</param>
        /// <param name="mapBin">The map binary data (ushort array from map.bin). Must not be null.</param>
        /// <param name="width">The map width in tiles. Must be positive.</param>
        /// <param name="height">The map height in tiles. Must be positive.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapId"/> or <paramref name="mapBin"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
        void LoadMapCollisionData(string mapId, ushort[] mapBin, int width, int height);
        
        /// <summary>
        /// Clears collision data for unloaded maps.
        /// </summary>
        /// <param name="mapId">The map identifier. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="mapId"/> is null.</exception>
        void UnloadMap(string mapId);
    }
}
```

#### TileInteractionCache

```csharp
/// <summary>
/// Caches tileset tile interactionIds by map position and elevation.
/// Provides fast O(1) lookup of interactionIds for collision checking.
/// </summary>
public interface ITileInteractionCache
{
    /// <summary>
    /// Gets the interactionId for a tileset tile at the given position.
    /// Returns null if no interactionId is set (normal tile behavior).
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="x">The X coordinate in tile space.</param>
    /// <param name="y">The Y coordinate in tile space.</param>
    /// <param name="elevation">The elevation level to check.</param>
    /// <returns>The interactionId (e.g., "base:interaction/tiles/ledge_south"), or null if none.</returns>
    string? GetTileInteractionId(string mapId, int x, int y, int elevation);
    
    /// <summary>
    /// Loads tile interaction data from map definition.
    /// Called when map is loaded. Eagerly populates cache for all tiles.
    /// </summary>
    void LoadMapTileInteractions(string mapId, MapDefinition mapDef, IMapDataService mapDataService);
    
    /// <summary>
    /// Clears tile interaction data for unloaded maps.
    /// </summary>
    void UnloadMap(string mapId);
}

public class TileInteractionCache : ITileInteractionCache
{
    // Map ID -> (X, Y, Elevation) -> interactionId
    private readonly Dictionary<string, Dictionary<(int x, int y, int elevation), string?>> _cache;
    private readonly IResourceManager _resourceManager;
    
    public TileInteractionCache(IResourceManager resourceManager)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _cache = new Dictionary<string, Dictionary<(int x, int y, int elevation), string?>>();
    }
    
    public string? GetTileInteractionId(string mapId, int x, int y, int elevation)
    {
        if (!_cache.TryGetValue(mapId, out var mapCache))
            return null;
        
        return mapCache.TryGetValue((x, y, elevation), out var interactionId) ? interactionId : null;
    }
    
    public void LoadMapTileInteractions(string mapId, MapDefinition mapDef, IMapDataService mapDataService)
    {
        var mapCache = new Dictionary<(int x, int y, int elevation), string?>();
        
        // Iterate through all layers at each elevation
        foreach (var layer in mapDef.Layers)
        {
            if (layer.Type != "tilelayer" || string.IsNullOrEmpty(layer.TileData))
                continue;
            
            // Decode tile data (GIDs)
            var gids = DecodeTileData(layer.TileData, layer.Width, layer.Height);
            
            // For each tile position, resolve GID to interactionId
            for (int y = 0; y < layer.Height; y++)
            {
                for (int x = 0; x < layer.Width; x++)
                {
                    int index = y * layer.Width + x;
                    int gid = gids[index];
                    
                    if (gid == 0)
                        continue; // Empty tile
                    
                    // Resolve GID to tileset and localTileId
                    var resolved = mapDataService.ResolveGidToTileset(mapId, gid);
                    if (resolved == null)
                        continue;
                    
                    var (tilesetId, localTileId) = resolved.Value;
                    
                    // Get tileset definition
                    var tilesetDef = _resourceManager.GetTilesetDefinition(tilesetId);
                    if (tilesetDef == null || localTileId >= tilesetDef.Tiles.Count)
                        continue;
                    
                    // Get interactionId from tileset tile
                    var tile = tilesetDef.Tiles[localTileId];
                    var interactionId = tile?.InteractionId ?? tile?.TileBehaviorId;
                    
                    if (interactionId != null)
                    {
                        mapCache[(x, y, layer.Elevation)] = interactionId;
                    }
                }
            }
        }
        
        _cache[mapId] = mapCache;
    }
    
    public void UnloadMap(string mapId)
    {
        _cache.Remove(mapId);
    }
}
```

**How it works**:
1. When map loads, `LoadMapTileInteractions()` is called
2. Iterate through all tile layers at each elevation
3. For each tile, resolve GID → tileset → localTileId → tileset tile → interactionId
4. Cache interactionId at (mapId, x, y, elevation)
5. During collision check, O(1) lookup from cache

#### IEntityPositionService (Entity Position Queries with Spatial Hash)

```csharp
/// <summary>
/// Service for querying entities by position using spatial hash.
/// Shared between CollisionService and InteractionSystem to avoid code duplication.
/// Provides O(1) lookups for efficient collision and interaction queries.
/// </summary>
public interface IEntityPositionService
{
    /// <summary>
    /// Gets all entities at a specific tile position with matching elevation.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    /// <param name="x">The X coordinate in tile space.</param>
    /// <param name="y">The Y coordinate in tile space.</param>
    /// <param name="elevation">The elevation level to match.</param>
    /// <returns>Read-only span of entities at the position (empty if none).</returns>
    ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y, int elevation);
    
    /// <summary>
    /// Gets all entities at a specific tile position (any elevation).
    /// </summary>
    ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y);
    
    /// <summary>
    /// Gets entities within a certain distance of a position (Manhattan distance).
    /// Useful for interaction system (find entities near player).
    /// </summary>
    ReadOnlySpan<Entity> GetEntitiesInRange(string mapId, int centerX, int centerY, int range, int elevation);
}
```

**Implementation**: `SpatialHashSystem` (see `collision-spatial-hash-analysis.md` for full implementation)

**Why Spatial Hash**:
- Maps have 1000+ tiles with multiple layers (elevations)
- Collision checks happen frequently (every movement request)
- O(1) lookup is much more efficient than O(n) iteration
- Even with ~100 entities, spatial hash avoids iterating all entities for each check
- Both collision and interaction systems benefit from the same optimization

**Note**: This service is shared with `InteractionSystem` to avoid duplicating entity position lookup logic. Both systems use the same `IEntityPositionService` interface.

### Collision Detection Flow

```csharp
public class CollisionService : ICollisionService
{
    private readonly ICollisionLayerCache _collisionLayerCache;
    private readonly IEntityPositionService _entityPositionService;
    private readonly IEntityElevationService _elevationService;
    
    public CollisionService(
        ICollisionLayerCache collisionLayerCache,
        IEntityPositionService entityPositionService,
        IEntityElevationService elevationService
    )
    {
        _collisionLayerCache = collisionLayerCache ?? throw new ArgumentNullException(nameof(collisionLayerCache));
        _entityPositionService = entityPositionService ?? throw new ArgumentNullException(nameof(entityPositionService));
        _elevationService = elevationService ?? throw new ArgumentNullException(nameof(elevationService));
    }
    
    public bool CanMoveTo(
        Entity entity,
        int targetX,
        int targetY,
        string? mapId,
        Direction fromDirection = Direction.None
    )
    {
        // 1. Validate inputs
        if (mapId == null)
            return false; // No map = blocked
        
        // 2. Bounds check (must be done before event to ensure valid position)
        if (!_collisionLayerCache.IsInBounds(mapId, targetX, targetY))
            return false; // Out of bounds = blocked
        
        // 3. Get entity elevation (via service to avoid World access)
        byte entityElevation = _elevationService.GetEntityElevation(entity);
        
        // 4. Fire collision check event (tile interaction scripts control movement)
        // Only fire if there are subscribers to avoid unnecessary allocation
        // Scripts subscribe to this event and can cancel/modify movement directly
        // Event is fired AFTER bounds check to ensure valid position
        // Note: Scripts can override ALL collision checks (intended for modding)
        // Note: Dynamic collision (moving platforms, state-based changes) is supported via events
        // Check if there are subscribers before creating event struct (performance optimization)
        if (EventBus.HasSubscribers<CollisionCheckEvent>())
        {
            var currentPos = _elevationService.GetEntityPosition(entity);
            var collisionCheckEvent = new CollisionCheckEvent
            {
                Entity = entity,
                CurrentPosition = currentPos,
                TargetPosition = (targetX, targetY),
                MapId = mapId,
                FromDirection = fromDirection,
                Elevation = entityElevation,
                IsBlocked = false,
                BlockReason = null
            };
            
            EventBus.Send(ref collisionCheckEvent);
            if (collisionCheckEvent.IsBlocked)
                return false; // Blocked by script (one-way tile, surf requirement, jump restriction, etc.)
        }
        
        // 5. Check tile collision override (bits 10-11 from map.bin)
        // This is checked BEFORE elevation mismatch
        // Any non-zero value (1-3) blocks movement
        byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
        bool isSolid = collisionValue.HasValue && collisionValue.Value > 0;
        
        if (isSolid)
        {
            // Solid tile - check elevation mismatch
            // Passable tiles (collision override = 0) allow movement regardless of elevation
            byte? tileElevation = _collisionLayerCache.GetElevation(mapId, targetX, targetY);
            if (tileElevation.HasValue)
            {
                if (!_collisionLayerCache.IsElevationMatch(entityElevation, tileElevation.Value))
                {
                    return false; // Elevation mismatch on solid tile
                }
            }
            return false; // Blocked by collision override
        }
        
        // Passable tile (collision override = 0) - no elevation check needed
        // Movement allowed, elevation can change naturally (handled by MovementSystem)
        
        // 6. Check entity collision (position query)
        // Note: ReadOnlySpan is only valid until next frame update
        var entitiesAtPosition = _entityPositionService.GetEntitiesAt(
            mapId, 
            targetX, 
            targetY, 
            entityElevation
        );
        
        foreach (var otherEntity in entitiesAtPosition)
        {
            if (otherEntity == entity)
                continue; // Skip self
            
            // Check if entity is still alive (might have been destroyed)
            if (!_elevationService.IsEntityAlive(otherEntity))
                continue;
            
            if (IsEntityBlocking(otherEntity))
                return false; // Blocked by entity
        }
        
        // All checks passed - movement allowed
        return true;
    }
    
    /// <summary>
    /// Checks if an entity blocks movement.
    /// Uses CollisionComponent if present, otherwise defaults based on entity type.
    /// </summary>
    private bool IsEntityBlocking(Entity entity)
    {
        // Check CollisionComponent if present
        if (_elevationService.TryGetCollisionComponent(entity, out var collision))
        {
            if (collision.AllowPassThrough)
                return false; // Ghost mode, allow pass-through
            
            return collision.IsSolid; // Use component value
        }
        
        // Default behavior: NPCs block, items don't
        if (_elevationService.HasComponent<NpcComponent>(entity))
            return true; // NPCs block by default
        
        return false; // Items and other entities don't block by default
    }
}
```

### IEntityElevationService

```csharp
namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Service for querying entity elevation and position.
    /// Wraps World access to avoid direct ECS coupling in CollisionService.
    /// </summary>
    public interface IEntityElevationService
    {
        /// <summary>
        /// Gets the elevation for an entity.
        /// Requires ElevationComponent - all entities must have this component.
        /// </summary>
        /// <param name="entity">The entity to query.</param>
        /// <returns>Entity elevation (0-15). Defaults to 0 (wildcard) if ElevationComponent not found.</returns>
        /// <exception cref="InvalidOperationException">Thrown if entity doesn't have ElevationComponent (fail fast).</exception>
        byte GetEntityElevation(Entity entity);
        
        /// <summary>
        /// Gets the current tile position of an entity.
        /// </summary>
        /// <param name="entity">The entity to query.</param>
        /// <returns>Current position (X, Y), or (0, 0) if entity not found or has no PositionComponent.</returns>
        (int X, int Y) GetEntityPosition(Entity entity);
        
        /// <summary>
        /// Checks if an entity is still alive (not destroyed).
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if entity is alive, false if destroyed.</returns>
        bool IsEntityAlive(Entity entity);
        
        /// <summary>
        /// Checks if an entity has a specific component.
        /// </summary>
        /// <typeparam name="T">The component type to check for.</typeparam>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if entity has the component, false otherwise.</returns>
        bool HasComponent<T>(Entity entity) where T : struct;
        
    /// <summary>
    /// Tries to get a CollisionComponent from an entity.
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <param name="component">When this method returns, contains the CollisionComponent if found; otherwise, the default value.</param>
    /// <returns>True if entity has CollisionComponent, false otherwise.</returns>
    bool TryGetCollisionComponent(Entity entity, out CollisionComponent component);
    
    /// <summary>
    /// Sets the elevation for an entity.
    /// Updates ElevationComponent if present, otherwise updates component-specific elevation.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="elevation">The new elevation value (0-15).</param>
    void SetEntityElevation(Entity entity, byte elevation);
    
    /// <summary>
    /// Tries to get an ElevationComponent from an entity.
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <param name="component">When this method returns, contains the ElevationComponent if found; otherwise, the default value.</param>
    /// <returns>True if entity has ElevationComponent, false otherwise.</returns>
    bool TryGetElevationComponent(Entity entity, out ElevationComponent component);
}
}
```

## Implementation Plan

### Phase 1: Core Collision Service

**Goal**: Implement basic tile collision checking with elevation support.

1. **Create `CollisionLayerCache`**
   - Load collision and elevation data from map.bin (per-tile, not per-elevation layer)
   - Extract collision override (bits 10-11) and elevation (bits 12-15) from each tile
   - Cache per map, store per-tile arrays
   - Provide O(1) lookup for collision and elevation
   - Implement `IsElevationMatch()` with special cases for elevation 0 (wildcard) and 15 (bridges)

2. **Implement `CollisionService`**
   - Implement `ICollisionService` interface
   - Basic tile collision checking
   - Check collision override BEFORE elevation mismatch (pokeemerald order)
   - Elevation mismatch checking with special cases
   - Bounds checking

3. **Integrate with MapLoaderSystem**
   - Load collision and elevation data from map.bin when maps load
   - Store in `CollisionLayerCache` (per-tile arrays)
   - Unload when maps unload

**Files to Create**:
- `MonoBall.Core/ECS/Services/ICollisionLayerCache.cs`
- `MonoBall.Core/ECS/Services/CollisionLayerCache.cs`
- `MonoBall.Core/ECS/Services/CollisionService.cs`
- `MonoBall.Core/ECS/Services/IEntityElevationService.cs`
- `MonoBall.Core/ECS/Services/EntityElevationService.cs`
- `MonoBall.Core/ECS/Components/ElevationComponent.cs` (required for all entities)
- `MonoBall.Core/ECS/Services/CollisionCheckEventPooledObjectPolicy.cs` (for event pooling)
- `MonoBall.Core/ECS/Events/CollisionCheckEvent.cs`

**Interface Updates**:
- `MonoBall.Core/ECS/Services/ICollisionService.cs` - Add `CanMoveToSilent()` method for pathfinding

**Files to Modify**:
- `MonoBall.Core/ECS/SystemManager.cs` - Register `CollisionService` instead of `NullCollisionService`
- `MonoBall.Core/ECS/Systems/MapLoaderSystem.cs` - Load collision data from map.bin

**Note on `CanMoveToSilent()`**:
- Performs same collision checks as `CanMoveTo()` but doesn't fire events
- Intended for pathfinding algorithms that need to check many positions
- Avoids event overhead and unintended script reactions
- Scripts cannot override collision in silent mode (by design)

### Phase 2: Spatial Hash System

**Goal**: Add entity-to-entity collision detection using spatial hash for efficient position queries.

1. **Create `IEntityPositionService` Interface**
   - Define interface for position-based entity queries
   - Supports exact position queries and range queries
   - Reusable by both CollisionService and InteractionSystem

2. **Create `SpatialHashSystem`**
   - Implements `IEntityPositionService` interface
   - Maintains spatial hash: Map ID → (X, Y, Elevation) → List<Entity>
   - Re-indexes dynamic entities each frame
   - Provides O(1) lookups for collision checks
   - Provides range queries for interaction system

3. **Entity Collision Checking**
   - Check if entities block movement
   - Support pass-through flags (via CollisionComponent)
   - Handle pushable objects

4. **Update InteractionSystem**
   - Refactor to use `IEntityPositionService` instead of manual queries
   - Use `GetEntitiesInRange()` for finding nearby entities
   - Reduces code duplication and improves performance

**Files to Create**:
- `MonoBall.Core/ECS/Services/IEntityPositionService.cs`
- `MonoBall.Core/ECS/Systems/SpatialHashSystem.cs`
- `MonoBall.Core/ECS/Components/CollisionComponent.cs` (optional, for entity collision flags)

**Files to Modify**:
- `MonoBall.Core/ECS/Services/CollisionService.cs` - Add entity collision checks
- `MonoBall.Core/ECS/Systems/InteractionSystem.cs` - Refactor to use `IEntityPositionService`

**Rationale**: With maps having 1000+ tiles and multiple layers, spatial hash provides significant performance benefits for frequent collision and interaction queries. See `collision-spatial-hash-analysis.md` for detailed analysis.

**InteractionSystem Refactoring**:
- Current: Queries all entities with `InteractionComponent`, filters by Manhattan distance
- Updated: Use `IEntityPositionService.GetEntitiesInRange()` to find entities near player
- Benefit: O(1) spatial hash lookup + range query instead of O(n) iteration over all entities
- Example: `GetEntitiesInRange(mapId, playerX, playerY, range: 1, elevation)` returns only entities within 1 tile

### Phase 3: Event-Driven Tile Interactions

**Goal**: Allow tile interaction scripts to directly control movement via events, not boolean queries.

1. **Create `TileInteractionCache`**
   - Lookup tileset tile at map position
   - Get `interactionId` from tileset tile definition
   - Cache tileset tile lookups for performance
   - Used to identify which tiles have interaction scripts

2. **Load and Attach Interaction Scripts**
   - When map loads, find tiles with `interactionId`
   - Load script from `interactionId` via DefinitionRegistry
   - Attach script to tile entity (or create virtual tile entity for script)
   - Scripts automatically subscribe to movement events in `RegisterEventHandlers()`

3. **Event-Driven Movement Control**
   - Scripts subscribe to `CollisionCheckEvent` (fired during collision checking)
   - Scripts can:
     - Cancel movement (`evt.IsBlocked = true`)
     - Modify movement (change direction, add jump, force movement) via movement API
     - Trigger effects (surf animation, jump animation, etc.)
   - No hardcoded behavior checks - scripts have full control

4. **Remove Hardcoded Behavior Logic**
   - Don't query scripts for `CanMoveFrom()` or `GetJumpDirection()`
   - Don't hardcode jump tile logic or surf tile logic
   - Let scripts handle all behavior through events
   - Collision system just fires events and checks results

**Files to Create**:
- `MonoBall.Core/ECS/Services/ITileInteractionCache.cs`
- `MonoBall.Core/ECS/Services/TileInteractionCache.cs` - Cache tileset tile interactionIds by position

**Files to Modify**:
- `MonoBall.Core/ECS/Services/CollisionService.cs` - Fire `CollisionCheckEvent`, remove script query logic
- `MonoBall.Core/ECS/Systems/MapLoaderSystem.cs` - Load and attach interaction scripts to tiles
- `MonoBall.Core/ECS/Events/CollisionCheckEvent.cs` - Ensure event supports script cancellation

### Phase 4: Event System Integration (Already in Phase 3)

**Note**: Event system integration is handled in Phase 3. `CollisionCheckEvent` is fired during collision checking, allowing scripts to control movement. No separate phase needed.

## Tile Interaction Scripts

### Event-Driven Approach

Tile interaction scripts (referenced by `interactionId` on tileset tiles) control movement by subscribing to movement events, not by returning boolean flags. This gives scripts full control over movement behavior.

### How It Works

1. **Scripts Attach to Tiles**: When a map loads, tiles with `interactionId` get their scripts attached
2. **Scripts Subscribe to Events**: Scripts subscribe to `CollisionCheckEvent` (fired during collision checking)
3. **Scripts Control Movement**: Scripts can:
   - **Cancel movement**: Set `evt.IsBlocked = true` to block movement
   - **Allow movement**: Do nothing (movement proceeds)
   - **Trigger effects**: Subscribe to `MovementCompletedEvent` to trigger animations/effects when movement completes
   - **Modify movement**: Use movement API to request additional movement (e.g., jumps)

**Key Point**: Scripts don't return boolean flags - they directly modify events to control movement behavior.

### Example: One-Way Tile (Impassable South)

```csharp
public class ImpassableSouthInteraction : ScriptBase
{
    protected override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to CollisionCheckEvent (fired during collision checking)
        On<CollisionCheckEvent>(evt =>
        {
            // Get tile position from script context (tile entity)
            var tilePos = GetTilePosition();
            if (tilePos == null)
                return;
            
            // Check if entity is moving onto this tile
            if (evt.TargetPosition.X != tilePos.Value.X || evt.TargetPosition.Y != tilePos.Value.Y)
                return; // Not moving to this tile
            
            // Block movement from north (can't walk through from north side)
            // Allow movement from south, east, west
            if (evt.FromDirection == Direction.North)
            {
                evt.IsBlocked = true;
                evt.BlockReason = "Cannot pass through from north";
            }
        });
    }
}
```

### Example: Jump Tile (Ledge South)

```csharp
public class LedgeSouthInteraction : ScriptBase
{
    protected override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to CollisionCheckEvent (fired during collision checking)
        On<CollisionCheckEvent>(evt =>
        {
            var tilePos = GetTilePosition();
            if (tilePos == null)
                return;
            
            // Check if entity is moving onto this tile
            if (evt.TargetPosition.X != tilePos.Value.X || evt.TargetPosition.Y != tilePos.Value.Y)
                return;
            
            // Only allow movement from north (opposite of jump direction)
            if (evt.FromDirection != Direction.North)
            {
                evt.IsBlocked = true;
                evt.BlockReason = "Can only jump from north";
                return;
            }
            
            // Movement allowed - script can trigger jump effect on MovementCompletedEvent
            // Or modify movement to add extra tile movement
        });
        
        // Subscribe to MovementCompletedEvent to trigger jump animation
        On<MovementCompletedEvent>(evt =>
        {
            var tilePos = GetTilePosition();
            if (tilePos == null)
                return;
            
            // Check if entity just completed movement onto this tile from north
            if (evt.NewPosition.X == tilePos.Value.X && 
                evt.NewPosition.Y == tilePos.Value.Y &&
                evt.Direction == Direction.North)
            {
                // Trigger jump: request additional movement south (jump 2 tiles)
                Context.Apis.Movement.RequestMovement(evt.Entity, Direction.South);
                // Jump animation/effect would be handled by animation system or script
            }
        });
    }
}
```

**Alternative Approach**: Scripts can also modify `MovementStartedEvent` to change the target position or add forced movement:

```csharp
public class LedgeSouthInteraction : ScriptBase
{
    protected override void RegisterEventHandlers(ScriptContext context)
    {
        On<CollisionCheckEvent>(evt =>
        {
            var tilePos = GetTilePosition();
            if (tilePos == null)
                return;
            
            if (evt.TargetPosition.X != tilePos.Value.X || evt.TargetPosition.Y != tilePos.Value.Y)
                return;
            
            if (evt.FromDirection != Direction.North)
            {
                evt.IsBlocked = true;
                return;
            }
            
            // Allow movement - jump will be handled by MovementCompletedEvent
        });
        
        // When movement completes onto this tile, trigger jump
        On<MovementCompletedEvent>(evt =>
        {
            var tilePos = GetTilePosition();
            if (tilePos == null)
                return;
            
            if (evt.NewPosition.X == tilePos.Value.X && 
                evt.NewPosition.Y == tilePos.Value.Y &&
                evt.Direction == Direction.North)
            {
                // Force additional movement south (jump)
                // This creates a new movement request that will be processed next frame
                Context.Apis.Movement.RequestMovement(evt.Entity, Direction.South);
            }
        });
    }
}
```

### Example: Surf Tile (Deep Water)

```csharp
public class DeepWaterInteraction : ScriptBase
{
    protected override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to CollisionCheckEvent to block movement if surf not active
        On<CollisionCheckEvent>(evt =>
        {
            var tilePos = GetTilePosition();
            if (tilePos == null)
                return;
            
            // Check if entity is moving onto this tile
            if (evt.TargetPosition.X != tilePos.Value.X || evt.TargetPosition.Y != tilePos.Value.Y)
                return;
            
            // Check if player has surf HM and is using it
            if (!HasSurfActive(evt.Entity))
            {
                evt.IsBlocked = true;
                evt.BlockReason = "Need HM Surf to travel on water";
                return;
            }
            
            // Movement allowed - surf mode will be set by MovementCompletedEvent
        });
        
        // When movement completes onto water tile, set surf mode
        On<MovementCompletedEvent>(evt =>
        {
            var tilePos = GetTilePosition();
            if (tilePos == null)
                return;
            
            if (evt.NewPosition.X == tilePos.Value.X && evt.NewPosition.Y == tilePos.Value.Y)
            {
                // Set surf mode (would be handled by movement mode system)
                // Or trigger surf animation/effect
                TriggerSurfEffect(evt.Entity);
            }
        });
    }
    
    private bool HasSurfActive(Entity entity)
    {
        // Check game state/flags via API
        // Would use constants service or player API
        return Context.Apis.Player.HasHM(entity, "Surf");
    }
    
    private void TriggerSurfEffect(Entity entity)
    {
        // Trigger surf animation or set movement mode
        // This would be handled by a movement mode system or animation system
        // For now, scripts can fire custom events that other systems handle
    }
}
```

### Benefits of Event-Driven Approach

1. **Full Control**: Scripts can do anything - cancel, modify, trigger effects, etc.
2. **No Hardcoding**: No need to hardcode "jump tile" or "surf tile" logic in collision system
3. **Flexible**: Scripts can implement complex behaviors (e.g., conditional jumps, multi-tile movement)
4. **Moddable**: Mods can easily override or extend tile behaviors
5. **Consistent**: Uses same event system as other game systems

### Script Attachment Strategy

**Option A: Attach to Tile Entities** (Recommended)
- When map loads, create tile entities for tiles with `interactionId`
- Attach scripts to tile entities via script system
- Scripts receive `TileScriptContext` with tile position, elevation, and map ID
- Scripts automatically subscribe to events in `RegisterEventHandlers()`
- Scripts are disposed when map unloads (prevents memory leaks)
- Scripts can be hot-reloaded (re-attach on reload)

**Script Context**:
```csharp
public class TileScriptContext : ScriptContext
{
    /// <summary>
    /// The tile position this script is attached to.
    /// </summary>
    public (int X, int Y) TilePosition { get; }
    
    /// <summary>
    /// The elevation of this tile.
    /// </summary>
    public byte Elevation { get; }
    
    /// <summary>
    /// The map ID this tile belongs to.
    /// </summary>
    public string MapId { get; }
}
```

**Script Lifecycle**:
1. Map loads → Tile entities created for tiles with `interactionId`
2. Scripts instantiated and attached to tile entities
3. `Initialize(TileScriptContext)` called
4. `RegisterEventHandlers(TileScriptContext)` called
5. Scripts subscribe to `CollisionCheckEvent` and other events
6. Map unloads → Scripts disposed, subscriptions cleaned up

**Option B: Virtual Tile Scripts** (Not Recommended)
- Don't create tile entities
- Create script instances per `interactionId` per map
- Scripts track which tile positions they affect
- More memory efficient, but scripts need to check positions
- Harder to manage lifecycle and hot-reload

## Component Design

### Component Design

#### ElevationComponent (Recommended)

```csharp
/// <summary>
/// Component for entity elevation.
/// Recommended for all entities to ensure consistent elevation storage.
/// </summary>
public struct ElevationComponent
{
    /// <summary>
    /// The entity's elevation (0-15).
    /// 0 = wildcard (matches any tile elevation)
    /// 1-15 = normal elevations (must match exactly for solid tiles)
    /// </summary>
    public byte Elevation { get; set; }
}
```

**Usage**:
- All entities should have `ElevationComponent` for consistent elevation storage
- Falls back to constants/NpcComponent if component not present (for backward compatibility)

#### CollisionComponent (Optional)

```csharp
/// <summary>
/// Component for entity collision properties.
/// Optional - entities without this component use default behavior based on entity type.
/// </summary>
public struct CollisionComponent
{
    /// <summary>
    /// Whether the entity blocks movement.
    /// </summary>
    public bool IsSolid { get; set; }
    
    /// <summary>
    /// Whether the entity can be pushed.
    /// </summary>
    public bool IsPushable { get; set; }
    
    /// <summary>
    /// Whether the entity allows pass-through (ghost mode, etc.).
    /// </summary>
    public bool AllowPassThrough { get; set; }
    
    /// <summary>
    /// Collision type for special handling (water, grass, etc.).
    /// </summary>
    public string? CollisionType { get; set; }
}
```

**Usage**:
- NPCs: `IsSolid = true` (block movement) or no component (defaults to blocking)
- Items: No component (don't block by default)
- Pushable objects: `IsSolid = true, IsPushable = true`
- Ghost mode: `AllowPassThrough = true`

## Event Design

### CollisionCheckEvent

```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired during collision checking to allow mods and scripts to override behavior.
    /// Cancellable - set IsBlocked = true to prevent movement.
    /// Fired AFTER bounds check to ensure valid position.
    /// </summary>
    /// <remarks>
    /// Event Priority Guidelines:
    /// - Priority 1000+: Core collision checks (run first)
    /// - Priority 500: Normal tile interaction scripts
    /// - Priority 0: Mods and effects (run last)
    /// 
    /// Note: Multiple handlers can modify IsBlocked. Handlers should check current state
    /// before modifying to avoid race conditions. Last handler wins.
    /// 
    /// Immutability: Most properties are immutable (init-only) to prevent accidental modification.
    /// Only IsBlocked and BlockReason are mutable (set) to allow handlers to modify the event.
    /// </remarks>
    public struct CollisionCheckEvent
    {
        /// <summary>
        /// The entity attempting to move.
        /// </summary>
        public Entity Entity { get; init; }
        
        /// <summary>
        /// The current tile position (X, Y) before movement.
        /// </summary>
        public (int X, int Y) CurrentPosition { get; init; }
        
        /// <summary>
        /// The target tile position (X, Y) after movement.
        /// </summary>
        public (int X, int Y) TargetPosition { get; init; }
        
        /// <summary>
        /// The map identifier (non-null, validated before event fires).
        /// </summary>
        public string MapId { get; init; }
        
        /// <summary>
        /// The direction moving FROM (for directional collision).
        /// </summary>
        public Direction FromDirection { get; init; }
        
        /// <summary>
        /// The entity's elevation (0-15).
        /// </summary>
        public byte Elevation { get; init; }
        
        /// <summary>
        /// Whether the collision check resulted in blocking.
        /// Mods can set this to true to block movement.
        /// Scripts can set this to false to allow movement (bypasses all collision checks).
        /// </summary>
        /// <remarks>
        /// WARNING: Setting IsBlocked = false allows movement through ALL collision
        /// (tiles, elevation, entities). This is intended for modding but can be used for cheats.
        /// </remarks>
        public bool IsBlocked { get; set; }
        
        /// <summary>
        /// Reason for blocking (for debugging/logging).
        /// </summary>
        public string? BlockReason { get; set; }
    }
}
```

**Usage Example**:
```csharp
// In a script (tile interaction)
On<CollisionCheckEvent>(evt =>
{
    // Check if entity is moving onto this tile
    var tilePos = GetTilePosition();
    if (evt.TargetPosition.X != tilePos.X || evt.TargetPosition.Y != tilePos.Y)
        return; // Not moving to this tile
    
    // Block movement from specific direction
    if (evt.FromDirection == Direction.North)
    {
        evt.IsBlocked = true;
        evt.BlockReason = "Cannot pass through from north";
    }
});

// In a mod (ghost mode)
On<CollisionCheckEvent>(evt =>
{
    if (evt.Entity == playerEntity && IsGhostModeActive())
    {
        // Check if already blocked (avoid race conditions)
        if (!evt.IsBlocked)
        {
            // Allow movement through all collision (cheat potential)
            evt.IsBlocked = false;
        }
    }
}, priority: 0); // Low priority - runs after core checks
```

### CollisionDetectedEvent

```csharp
/// <summary>
/// Event fired when a collision is detected (informational, not cancellable).
/// Useful for triggering effects, sounds, or interactions.
/// </summary>
public struct CollisionDetectedEvent
{
    /// <summary>
    /// The entity that collided.
    /// </summary>
    public Entity Entity { get; set; }
    
    /// <summary>
    /// The collision position (X, Y).
    /// </summary>
    public (int X, int Y) Position { get; set; }
    
    /// <summary>
    /// The map identifier.
    /// </summary>
    public string? MapId { get; set; }
    
    /// <summary>
    /// The type of collision (Tile, Entity, etc.).
    /// </summary>
    public CollisionType Type { get; set; }
    
    /// <summary>
    /// The other entity involved (if Type == Entity).
    /// </summary>
    public Entity? OtherEntity { get; set; }
}
```

## Performance Considerations

### Optimization Strategies

1. **Spatial Hash**
   - O(1) entity lookups instead of O(n) iteration
   - Only check entities at target position
   - Elevation filtering reduces checks

2. **Collision Layer Caching**
   - Pre-compute collision and elevation data at map load
   - Store as byte arrays (1 byte per tile per value)
   - O(1) lookup: `collisionData[y * width + x]`, `elevationData[y * width + x]`
   - **Combined lookup**: `GetTileData()` returns both values in one call (reduces cache lookups by ~50%)

3. **Event Optimization**
   - **Check subscribers first**: `EventBus.HasSubscribers<CollisionCheckEvent>()` before allocating event
   - Only get entity position if subscribers exist (avoids unnecessary ECS query)
   - Events are structs (zero-allocation when passed by ref)

4. **Query Caching**
   - Cache `QueryDescription` in systems (if queries are needed)
   - Reuse collections (clear instead of allocate)
   - Batch entity queries when possible

5. **Early Exit**
   - Check bounds first (fastest check)
   - Check tile collision before elevation (collision is more common blocker)
   - Skip entity checks if tile is already blocked
   - Fire event AFTER bounds check (avoid invalid position queries)
   - **Skip elevation check for passable tiles** (collision override = 0)

6. **Silent Collision Queries**
   - Provide `CanMoveToSilent()` method for pathfinding that doesn't fire events
   - Reduces event overhead for bulk queries

### Performance Targets

- **Collision Check**: < 0.1ms per check (target: < 0.01ms)
- **Collision Check (Silent)**: < 0.05ms per check (no events)
- **Spatial Hash Update**: < 1ms per frame (for 1000 entities)
- **Memory**: < 2MB per map (for collision + elevation data)

## Testing Strategy

### Unit Tests

1. **CollisionLayerCache**
   - Test bounds checking
   - Test elevation filtering
   - Test out-of-bounds positions

2. **CollisionService**
   - Test basic tile collision (0-3 values)
   - Test elevation matching
   - Test entity collision
   - Test directional collision (one-way tiles)

3. **SpatialHashSystem**
   - Test entity indexing
   - Test elevation filtering
   - Test entity queries

### Integration Tests

1. **Movement + Collision**
   - Player blocked by wall
   - Player blocked by NPC
   - Player can move through passable tiles

2. **Elevation System**
   - Player on elevation 3 collides with elevation 3 tiles
   - Player on elevation 3 CAN walk on elevation 0 tiles (ground level matches any elevation)
   - Player on elevation 3 CAN walk on passable tiles at any elevation (collision override = 0)
   - Player on elevation 3 CANNOT walk on solid tiles at elevation 4 (elevation mismatch on solid tile)

3. **Event System**
   - Mods can override collision
   - Events fire correctly

## Future Enhancements

1. **Advanced Tile Behaviors**
   - Water tiles (require surf) - handled by interaction scripts
   - Grass tiles (encounters) - handled by interaction scripts
   - Ice tiles (sliding) - handled by interaction scripts

2. **Multi-Tile Entities**
   - Large entities spanning multiple tiles
   - Partial collision (only certain parts block)
   - Requires spatial hash updates to support multi-tile bounds

3. **Collision Debugging**
   - Visual collision overlay
   - Collision statistics
   - Performance profiling
   - Event subscription tracking

## Known Limitations and Trade-offs

1. **Script Override Capability**
   - Scripts can bypass ALL collision checks (intended for modding)
   - No way to prevent mods from creating cheats (by design)
   - Consider adding "core collision" flag in future if security is needed

2. **Event Firing Behavior**
   - Events only fire if there are subscribers (checked via `EventBus.HasSubscribers<CollisionCheckEvent>()`)
   - When a script subscribes to `CollisionCheckEvent`, it adds a handler to EventBus's internal cache
   - `HasSubscribers()` checks this cache - if true, subscribers exist and events will fire
   - When subscribers exist, events fire for every collision check (intentional for script control)
   - Scripts work correctly: subscription → cache updated → `HasSubscribers()` returns true → event fires → script handler executes
   - Use `CanMoveToSilent()` for pathfinding to avoid triggering script handlers and event overhead

3. **ReadOnlySpan Lifetime**
   - `GetEntitiesAt()` returns `ReadOnlySpan<Entity>` which is only valid until next frame
   - Document clearly that spans must be used immediately

## References

- **Pokemon Emerald**: Original collision system reference
- **oldmonoball**: Previous implementation with spatial hash
- **Arch ECS**: Entity Component System library
- **MonoGame**: Game framework

## Appendix: Constants and Magic Numbers

### Elevation Constants

Consider defining constants for elevation special cases:

```csharp
namespace MonoBall.Core.ECS.Constants
{
    /// <summary>
    /// Constants for elevation values used in collision and rendering.
    /// </summary>
    public static class ElevationConstants
    {
        /// <summary>
        /// Minimum elevation value (0).
        /// Also serves as wildcard (entity) - matches any tile elevation.
        /// Also serves as ground level (tile) - matches any entity elevation.
        /// </summary>
        public const byte Min = 0;
        
        /// <summary>
        /// Maximum elevation value (15).
        /// </summary>
        public const byte Max = 15;
    }
}
```

### Collision Value Meanings

From Pokemon Emerald:
- **0**: Passable (no collision)
- **1**: Blocked (solid wall)
- **2**: Blocked (solid wall, different visual)
- **3**: Blocked (solid wall, different visual)

Note: Values 1-3 are functionally identical (all block movement). The distinction is for visual/editor purposes only.

Consider defining constants:

```csharp
namespace MonoBall.Core.ECS.Constants
{
    /// <summary>
    /// Constants for collision override values from map.bin.
    /// </summary>
    public static class CollisionConstants
    {
        /// <summary>
        /// Passable - no collision override.
        /// </summary>
        public const byte Passable = 0;
        
        /// <summary>
        /// Blocked - collision override (all values 1-3 are treated the same).
        /// </summary>
        public const byte Blocked = 1;  // Or 2, or 3 - all equivalent
    }
}
```

