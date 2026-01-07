# Collision System Design Analysis

## Overview

This document analyzes the collision system design for architectural issues, naming inconsistencies, structural problems, missing pieces, and potential improvements.

## Critical Issues

### 1. **Collision Detection Logic Error**

**Issue**: The collision detection flow has incorrect logic for handling collision layers vs interactions.

**Current Flow (Lines 251-273)**:
```csharp
if (collisionValue.HasValue && collisionValue.Value > 0)
{
    // Tile has collision - check if interaction allows movement
    string? interactionId = _tileInteractionLookup.GetTileInteractionId(...);
    
    if (interactionId != null)
    {
        // Query interaction script
        if (!CheckTileInteraction(...))
            return false; // Blocked by interaction
    }
    else
    {
        // No interaction override - collision layer value blocks movement
        return false; // Blocked by collision layer
    }
}
```

**Problem**: 
- If collision layer says "blocked" (value > 0) but no interactionId exists, it blocks movement. This is correct.
- However, if collision layer says "passable" (value == 0), the code never checks interactions, which means one-way tiles and jump tiles won't work if the collision layer is passable.

**Fix**: Interactions should be checked regardless of collision layer value. The logic should be:
1. Check collision layer (if blocked and no interaction override, block)
2. Check interaction (can override collision layer, or add restrictions even if passable)
3. If collision layer is passable AND no interaction restrictions, allow movement

**Proposed Flow**:
```csharp
// Always check interactions first (they can override collision layer)
string? interactionId = _tileInteractionLookup.GetTileInteractionId(
    mapId, targetX, targetY, entityElevation
);

if (interactionId != null)
{
    // Interaction exists - check directional collision
    if (!CheckTileInteraction(entity, interactionId, fromDirection))
        return false; // Blocked by interaction (one-way, jump, etc.)
}

// If no interaction restrictions, check collision layer
byte? collisionValue = _collisionLayerCache.GetCollisionValue(
    mapId, entityElevation, targetX, targetY
);

if (collisionValue.HasValue && collisionValue.Value > 0)
{
    return false; // Blocked by collision layer
}
```

### 2. **Missing GID Resolution Strategy**

**Issue**: `TileInteractionLookup` needs to resolve map position → GID → tileset tile → interactionId, but the design doesn't explain how.

**Missing Details**:
- How to get GID from map layer at (x, y, elevation)?
- How to resolve GID to tileset (need `TilesetReference` lookup)?
- How to get `localTileId` from GID (GID - firstGid)?
- How to lookup `TilesetTile` from tileset definition?

**Proposed Solution**:
```csharp
public class TileInteractionLookup
{
    private readonly IMapDataService _mapDataService; // Provides GID lookup
    private readonly IResourceManager _resourceManager; // Provides tileset definitions
    
    public string? GetTileInteractionId(string mapId, int x, int y, int elevation)
    {
        // 1. Get GID from map layer at position
        int? gid = _mapDataService.GetGidAtPosition(mapId, x, y, elevation);
        if (gid == null || gid.Value == 0)
            return null; // Empty tile
        
        // 2. Resolve GID to tileset and localTileId
        var (tilesetId, localTileId) = ResolveGidToTileset(mapId, gid.Value);
        if (tilesetId == null)
            return null;
        
        // 3. Get tileset definition
        var tilesetDef = _resourceManager.GetTilesetDefinition(tilesetId);
        if (tilesetDef == null || localTileId >= tilesetDef.Tiles.Count)
            return null;
        
        // 4. Get tile interactionId
        var tile = tilesetDef.Tiles[localTileId];
        return tile?.InteractionId; // or TileBehaviorId
    }
}
```

### 3. **SpatialHashSystem Architecture Mismatch**

**Issue**: `SpatialHashSystem` is described as a `BaseSystem<World, float>` but used as a service dependency.

**Problem**: 
- Systems shouldn't be dependencies of services (violates dependency direction)
- Services should depend on interfaces, not concrete systems
- The system needs to update every frame, but collision service needs to query it

**Proposed Solution**:
- Create `ISpatialQueryService` interface
- `SpatialHashSystem` implements the interface AND inherits from `BaseSystem`
- `CollisionService` depends on `ISpatialQueryService`, not the system directly

```csharp
public interface ISpatialQueryService
{
    ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y);
    ReadOnlySpan<Entity> GetEntitiesAtElevation(string mapId, int x, int y, int elevation);
}

public class SpatialHashSystem : BaseSystem<World, float>, ISpatialQueryService
{
    // Implementation
}
```

### 4. **Missing Entity Elevation Resolution**

**Issue**: `GetEntityElevation(entity)` is called but not defined.

**Problem**: How do we get elevation for different entity types?
- Player: from constants (`PlayerElevation`)
- NPCs: from `NpcComponent.Elevation`
- Other entities: ???

**Proposed Solution**:
```csharp
private int GetEntityElevation(Entity entity)
{
    // Check PlayerComponent first
    if (World.TryGet<PlayerComponent>(entity, out _))
    {
        return _constants.Get<int>("PlayerElevation");
    }
    
    // Check NpcComponent
    if (World.TryGet<NpcComponent>(entity, out var npc))
    {
        return npc.Elevation;
    }
    
    // Default elevation (could be configurable)
    return 0;
}
```

## Naming & Structure Issues

### 5. **TileInteractionLookup Naming**

**Issue**: "Lookup" implies a simple dictionary lookup, but it's actually a cache with complex resolution logic.

**Proposed**: Rename to `TileInteractionCache` or `MapTileInteractionCache` to better reflect its purpose.

### 6. **CollisionLayerCache vs TileInteractionCache**

**Issue**: Inconsistent naming - one uses "Cache", one uses "Lookup".

**Proposed**: Use consistent naming:
- `CollisionLayerCache` ✓ (keep)
- `TileInteractionCache` (rename from `TileInteractionLookup`)

### 7. **Missing Service Interface**

**Issue**: `CollisionService` implements `ICollisionService`, but `CollisionLayerCache` and `TileInteractionCache` don't have interfaces.

**Proposed**: Create interfaces for testability and flexibility:
- `ICollisionLayerCache`
- `ITileInteractionCache`

## Missing Pieces

### 8. **Interaction Script Instantiation**

**Issue**: Design mentions querying interaction scripts but doesn't explain how scripts are instantiated/cached.

**Problem**: 
- Scripts need to be instantiated from `ScriptDefinition`
- Should scripts be cached or instantiated per query?
- How do scripts access game state (surf HM, etc.)?

**Proposed**: 
- Create `ITileInteractionScriptFactory` service
- Cache script instances per `interactionId`
- Scripts receive context (entity, world, APIs) when methods are called

### 9. **Multiple Tiles Per Position**

**Issue**: Metatiles consist of 4 tiles (2x2), but design doesn't address which tile's interactionId to use.

**Problem**: At position (x, y), there might be multiple tiles from different elevations or layers.

**Proposed**: 
- Use elevation to filter which layer to check
- For metatiles, use the "primary" tile's interactionId (or combine logic)
- Document that interactions are per-tile, not per-metatile

### 10. **Missing Error Handling**

**Issue**: No error handling strategy for:
- Missing tileset definitions
- Missing interaction scripts
- Invalid GIDs
- Missing collision layers

**Proposed**: Fail-fast with clear exceptions:
```csharp
if (tilesetDef == null)
{
    throw new InvalidOperationException(
        $"Tileset '{tilesetId}' not found for map '{mapId}' at position ({x}, {y})"
    );
}
```

### 11. **Event Timing**

**Issue**: `CollisionCheckEvent` is fired at the end of the pipeline, but it's described as "before collision check".

**Problem**: Events should fire early to allow complete override, not just final veto.

**Proposed**: Fire event at the beginning:
```csharp
// 1. Event system (mod injection - can override everything)
var collisionCheckEvent = new CollisionCheckEvent { ... };
EventBus.Send(ref collisionCheckEvent);
if (collisionCheckEvent.IsBlocked)
    return false;

// 2. Bounds check
// 3. Elevation match
// ... rest of pipeline
```

## Performance Concerns

### 12. **TileInteractionCache Population**

**Issue**: Design says "cache tileset tile lookups" but doesn't specify when/how cache is populated.

**Problem**: 
- Should cache be populated at map load time (eager)?
- Or populated on-demand (lazy)?
- How much memory will this use?

**Proposed**: 
- **Eager loading**: Populate cache when map loads (better performance, more memory)
- Cache key: `(mapId, x, y, elevation)` → `interactionId`
- Memory estimate: ~4 bytes per tile position = ~16KB for 64x64 map

### 13. **Script Method Calls in Hot Path**

**Issue**: Calling script methods (`CanMoveFrom()`, `GetJumpDirection()`) in collision check hot path.

**Problem**: Script execution might be slow, especially if scripts access game state.

**Proposed**: 
- Cache script instances (don't instantiate per call)
- Keep script methods lightweight (no heavy game state queries)
- Consider pre-computing common interaction rules

### 14. **Multiple Dictionary Lookups**

**Issue**: Collision check does multiple dictionary lookups:
1. `CollisionLayerCache` lookup
2. `TileInteractionCache` lookup  
3. `SpatialHashSystem` lookup
4. Script registry lookup

**Proposed**: 
- Profile to ensure lookups are fast (they should be O(1))
- Consider combining caches if lookup patterns align

## Integration Issues

### 15. **MapLoaderSystem Integration**

**Issue**: Design says "integrate with MapLoaderSystem" but doesn't specify how.

**Proposed**: 
- `MapLoaderSystem` calls `CollisionLayerCache.LoadMapCollisionLayers()` when map loads
- `MapLoaderSystem` calls `TileInteractionCache.LoadMapTileInteractions()` when map loads
- Both caches implement `IMapLoadListener` interface for loose coupling

### 16. **Missing Map Data Service**

**Issue**: Need to lookup GIDs from map layers, but no service exists for this.

**Proposed**: Create `IMapDataService`:
```csharp
public interface IMapDataService
{
    /// <summary>
    /// Gets the GID at a specific map position and elevation.
    /// Returns null if position is out of bounds or tile is empty.
    /// </summary>
    int? GetGidAtPosition(string mapId, int x, int y, int elevation);
    
    /// <summary>
    /// Resolves a GID to its tileset and local tile ID.
    /// </summary>
    (string? tilesetId, int localTileId)? ResolveGidToTileset(string mapId, int gid);
}
```

## Design Improvements

### 17. **Separate Collision Types**

**Issue**: All collision checks are in one method, making it hard to understand flow.

**Proposed**: Split into separate methods:
```csharp
private bool CheckBounds(string mapId, int x, int y);
private bool CheckCollisionLayer(string mapId, int x, int y, int elevation);
private bool CheckTileInteraction(string mapId, int x, int y, int elevation, Direction fromDirection);
private bool CheckEntityCollision(string mapId, int x, int y, int elevation, Entity entity);
```

### 18. **CollisionResult Type**

**Issue**: `GetTileCollisionInfo()` returns a tuple, which is hard to extend.

**Proposed**: Create a result type:
```csharp
public struct TileCollisionInfo
{
    public bool IsWalkable { get; set; }
    public bool IsJumpTile { get; set; }
    public Direction AllowedJumpDirection { get; set; }
    public string? RequiredMovementMode { get; set; }
    public string? BlockReason { get; set; }
}
```

### 19. **Elevation Component**

**Issue**: Elevation is stored in different places (constants, NpcComponent, etc.).

**Proposed**: Create `ElevationComponent` for all entities:
```csharp
public struct ElevationComponent
{
    public int Elevation { get; set; }
}
```

This provides consistency and makes elevation queries easier.

## Summary of Proposed Changes

### High Priority
1. Fix collision detection logic (interactions should be checked regardless of collision layer)
2. Add GID resolution strategy to `TileInteractionCache`
3. Create `ISpatialQueryService` interface
4. Implement `GetEntityElevation()` method
5. Add `IMapDataService` for GID lookups

### Medium Priority
6. Rename `TileInteractionLookup` → `TileInteractionCache`
7. Create interfaces for cache services
8. Add interaction script factory/caching
9. Fix event timing (fire early, not late)
10. Add error handling with fail-fast exceptions

### Low Priority
11. Split collision checks into separate methods
12. Create `TileCollisionInfo` result type
13. Consider `ElevationComponent` for consistency
14. Document cache population strategy
15. Add integration points for `MapLoaderSystem`

## Recommended Next Steps

1. **Update design document** with fixes for critical issues (#1-4)
2. **Create service interfaces** (`ISpatialQueryService`, `IMapDataService`, `ITileInteractionCache`)
3. **Clarify GID resolution** in `TileInteractionCache` design
4. **Fix collision logic** to check interactions regardless of collision layer
5. **Add error handling** strategy throughout

