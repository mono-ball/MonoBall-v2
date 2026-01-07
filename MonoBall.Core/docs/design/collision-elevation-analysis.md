# Collision and Elevation Analysis: Our Design vs Pokeemerald-Expansion

## Executive Summary

After analyzing pokeemerald-expansion's collision system, we've identified several critical issues with our current design:

1. **Collision encoding**: Collision values (0-3) are stored per-tile in map.bin, not per-elevation layer
2. **Elevation special cases**: Elevation 0 and 15 are special (wildcard and bridges)
3. **Collision checking order**: Elevation mismatch is checked AFTER collision override, not before
4. **Collision value meaning**: Values 1-3 all mean "blocked" - there's no distinction between them in basic collision checking

## How Pokeemerald-Expansion Works

### Map.bin Structure

Each map.bin entry is a 16-bit value (`ushort`) with the following encoding:

```
Bits 0-9:   Metatile ID (0-1023)
Bits 10-11: Collision override (0-3)
Bits 12-15: Elevation (0-15)
```

**Key Point**: Collision and elevation are stored **per tile**, not in separate layers.

### Collision Override Values

From `pokeemerald-expansion/include/global.fieldmap.h`:
- **0**: Passable (no collision override)
- **1-3**: Blocked (all treated the same in basic collision checking)

**Important**: The collision override is a **2-bit value** (0-3), not separate layers. Values 1, 2, and 3 are all treated as "blocked" in `MapGridGetCollisionAt()`:

```c
u8 MapGridGetCollisionAt(int x, int y)
{
    u16 block = GetMapGridBlockAt(x, y);
    if (block == MAPGRID_UNDEFINED)
        return TRUE;  // Undefined = blocked
    
    return UNPACK_COLLISION(block);  // Returns 0-3
}
```

Then in collision checking:
```c
if (MapGridGetCollisionAt(x, y) || ...)  // Any non-zero value blocks
    return COLLISION_IMPASSABLE;
```

### Elevation Special Cases

From `IsElevationMismatchAt()`:

```c
bool8 IsElevationMismatchAt(u8 elevation, s16 x, s16 y)
{
    u8 mapElevation = MapGridGetElevationAt(x, y);
    
    if (elevation == 0)
        return FALSE;  // Elevation 0 = wildcard (matches any)
    
    if (mapElevation == 0 || mapElevation == 15)
        return FALSE;  // Elevation 0 or 15 = no mismatch
    
    if (mapElevation != elevation)
        return TRUE;   // Different elevations = mismatch
    
    return FALSE;
}
```

**Special Elevation Values**:
- **0 (entity)**: Wildcard - matches any tile elevation (allows walking at ground level)
- **0 (tile)**: Ground level - matches any entity elevation (allows walking on ground from any elevation)
- **1-15**: Normal elevations - must match exactly for solid tiles

**Important**: Elevation mismatch only blocks if collision override > 0 (solid tile). Passable tiles (collision override = 0) allow movement regardless of elevation, enabling natural elevation changes.

### Collision Checking Order

From `GetVanillaCollision()`:

```c
static u8 GetVanillaCollision(struct ObjectEvent *objectEvent, s16 x, s16 y, u8 direction)
{
    // 1. Check bounds/range
    if (IsCoordOutsideObjectEventMovementRange(objectEvent, x, y))
        return COLLISION_OUTSIDE_RANGE;
    
    // 2. Check collision override (bits 10-11)
    else if (MapGridGetCollisionAt(x, y) || GetMapBorderIdAt(x, y) == -1 || IsMetatileDirectionallyImpassable(...))
        return COLLISION_IMPASSABLE;
    
    // 3. Check camera movement (if tracked)
    else if (objectEvent->trackedByCamera && !CanCameraMoveInDirection(direction))
        return COLLISION_IMPASSABLE;
    
    // 4. Check elevation mismatch
    else if (IsElevationMismatchAt(objectEvent->currentElevation, x, y))
        return COLLISION_ELEVATION_MISMATCH;
    
    // 5. Check entity collision
    else if (DoesObjectCollideWithObjectAt(objectEvent, x, y))
        return COLLISION_OBJECT_EVENT;
    
    return COLLISION_NONE;
}
```

**Key Order**:
1. Bounds check
2. **Collision override** (bits 10-11)
3. Elevation mismatch
4. Entity collision

## Issues with Our Current Design

### Issue 1: Collision Layer Structure

**Our Design**: We build separate collision layers per elevation:
```csharp
// Build collision layers from map binary data, one per elevation level
private static List<CollisionLayerData> BuildCollisionLayers(...)
{
    // Group tiles by elevation and check if any collision data exists at that elevation
    var elevationData = new Dictionary<int, byte[]>();
    // Creates separate layers per elevation
}
```

**Problem**: This assumes collision is stored per-elevation layer, but in pokeemerald, collision is stored **per tile** with elevation. Each tile has its own collision value and elevation value.

**Correct Approach**: Collision should be stored per-tile, and we query it by checking the tile's elevation matches the entity's elevation.

### Issue 2: Collision Value Encoding

**Our Design**: We treat collision values 0-3 as distinct:
```csharp
if (collisionValue.HasValue && collisionValue.Value > 0)
{
    return false; // Blocked by collision layer
}
```

**Pokeemerald Reality**: Values 1-3 are all treated as "blocked". The distinction might be for future use or specific behaviors, but basic collision checking treats any non-zero as blocked.

**Our Code is Correct**: We already treat `> 0` as blocked, which matches pokeemerald.

### Issue 3: Elevation Mismatch Logic

**Our Design**: We check elevation early and filter by elevation:
```csharp
// 3. Get entity elevation
int entityElevation = GetEntityElevation(entity);

// 5. Check tile collision layer (if no script restrictions)
byte? collisionValue = _collisionLayerCache.GetCollisionValue(
    mapId, 
    entityElevation,  // Filtering by elevation
    targetX, 
    targetY
);
```

**Problem**: We're filtering collision layers by elevation, but in pokeemerald:
1. Collision is stored per-tile (not per-elevation layer)
2. Elevation mismatch is checked **after** collision override
3. Elevation 0 is a special case (wildcard/ground level)

**Correct Approach**: 
1. Get collision value from tile (regardless of elevation)
2. Check collision override first
3. Then check elevation mismatch ONLY if collision override > 0 (solid tile)
4. Passable tiles (collision override = 0) allow movement regardless of elevation mismatch
5. This enables natural elevation changes via stairs, bridges, etc.

### Issue 4: Elevation Special Cases Missing

**Our Design**: We don't handle elevation 0 (wildcard) or 15 (bridges) specially.

**Pokeemerald Reality**:
- Elevation 0 = wildcard (matches any elevation)
- Elevation 15 = bridges (special rendering/collision)

**Fix Needed**: Add special handling for elevation 0 (wildcard/ground level). Elevation mismatch only blocks if collision override > 0 (solid tile).

## Corrected Design

### Collision Data Structure

Instead of separate layers per elevation, store collision per-tile:

```csharp
public interface ICollisionLayerCache
{
    /// <summary>
    /// Gets the collision override value (0-3) for a tile at the given position.
    /// Returns null if position is out of bounds.
    /// </summary>
    byte? GetCollisionValue(string mapId, int x, int y);
    
    /// <summary>
    /// Gets the elevation value (0-15) for a tile at the given position.
    /// Returns null if position is out of bounds.
    /// </summary>
    byte? GetElevation(string mapId, int x, int y);
    
    /// <summary>
    /// Checks if an entity elevation matches a tile elevation.
    /// Handles special cases: elevation 0 (wildcard and ground level).
    /// </summary>
    /// <remarks>
    /// Elevation mismatch only blocks if collision override > 0 (solid tile).
    /// Passable tiles (collision override = 0) allow movement regardless of elevation,
    /// enabling natural elevation changes via stairs, bridges, etc.
    /// </remarks>
    bool IsElevationMatch(byte entityElevation, byte tileElevation);
}
```

### Collision Checking Flow (Corrected)

```csharp
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
        return false;
    
    // 2. Bounds check
    if (!_collisionLayerCache.IsInBounds(mapId, targetX, targetY))
        return false;
    
    // 3. Get entity elevation
    byte entityElevation = GetEntityElevation(entity);
    
    // 4. Fire collision check event (scripts can cancel/modify)
    var collisionCheckEvent = new CollisionCheckEvent
    {
        Entity = entity,
        TargetPosition = (targetX, targetY),
        MapId = mapId,
        FromDirection = fromDirection,
        Elevation = entityElevation,
        IsBlocked = false
    };
    EventBus.Send(ref collisionCheckEvent);
    if (collisionCheckEvent.IsBlocked)
        return false;
    
    // 5. Check collision override (bits 10-11 from map.bin)
    // This is checked BEFORE elevation mismatch (pokeemerald order)
    byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
    if (collisionValue.HasValue && collisionValue.Value > 0)
    {
        // Any non-zero collision value (1-3) blocks movement
        return false;
    }
    
    // 6. Check elevation mismatch (only if collision override blocks)
    // If collision override = 0 (passable), elevation mismatch doesn't block movement.
    // This allows entities to walk under bridges, change elevation via stairs, etc.
    byte? tileElevation = _collisionLayerCache.GetElevation(mapId, targetX, targetY);
    if (tileElevation.HasValue && collisionValue.HasValue && collisionValue.Value > 0)
    {
        // Only check elevation mismatch if tile is solid (collision override > 0)
        // Passable tiles (collision override = 0) allow movement regardless of elevation
        if (!_collisionLayerCache.IsElevationMatch(entityElevation, tileElevation.Value))
        {
            return false; // Elevation mismatch on solid tile
        }
    }
    
    // 7. Check entity collision
    var entitiesAtPosition = _entityPositionService.GetEntitiesAt(
        mapId, 
        targetX, 
        targetY, 
        entityElevation
    );
    
    foreach (var otherEntity in entitiesAtPosition)
    {
        if (otherEntity == entity)
            continue;
        
        if (IsEntityBlocking(otherEntity))
            return false;
    }
    
    return true;
}

/// <summary>
/// Checks if entity elevation matches tile elevation.
/// Handles special cases: elevation 0 (wildcard and ground level).
/// </summary>
/// <remarks>
/// Elevation mismatch only blocks if collision override > 0 (solid tile).
/// Passable tiles (collision override = 0) allow movement regardless of elevation,
/// enabling natural elevation changes via stairs, bridges, etc.
/// </remarks>
private bool IsElevationMatch(byte entityElevation, byte tileElevation)
{
    // Elevation 0 (entity) = wildcard (matches any tile elevation)
    if (entityElevation == 0)
        return true;
    
    // Elevation 0 (tile) = ground level (matches any entity elevation)
    if (tileElevation == 0)
        return true;
    
    // Must match exactly for collision
    return entityElevation == tileElevation;
}
```

### CollisionLayerCache Implementation

```csharp
public class CollisionLayerCache : ICollisionLayerCache
{
    // Store per-tile collision and elevation (not per-elevation layers)
    private readonly Dictionary<string, (byte[] collision, byte[] elevation, int width, int height)> _cache = new();
    
    public void LoadMap(string mapId, ushort[] mapBin, int width, int height)
    {
        var collision = new byte[width * height];
        var elevation = new byte[width * height];
        
        for (int i = 0; i < mapBin.Length; i++)
        {
            var entry = mapBin[i];
            collision[i] = (byte)MapBinReader.GetCollision(entry);  // Bits 10-11
            elevation[i] = (byte)MapBinReader.GetElevation(entry);  // Bits 12-15
        }
        
        _cache[mapId] = (collision, elevation, width, height);
    }
    
    public byte? GetCollisionValue(string mapId, int x, int y)
    {
        if (!_cache.TryGetValue(mapId, out var data))
            return null;
        
        if (x < 0 || x >= data.width || y < 0 || y >= data.height)
            return null;
        
        int index = y * data.width + x;
        return data.collision[index];
    }
    
    public byte? GetElevation(string mapId, int x, int y)
    {
        if (!_cache.TryGetValue(mapId, out var data))
            return null;
        
        if (x < 0 || x >= data.width || y < 0 || y >= data.height)
            return null;
        
        int index = y * data.width + x;
        return data.elevation[index];
    }
    
    public bool IsElevationMatch(byte entityElevation, byte tileElevation)
    {
        // Elevation 0 = wildcard (matches any)
        if (entityElevation == 0)
            return true;
        
        // Elevation 0 or 15 on tile = no mismatch
        if (tileElevation == 0 || tileElevation == 15)
            return true;
        
        // Must match exactly
        return entityElevation == tileElevation;
    }
}
```

## Summary of Required Changes

1. **Change collision storage**: Store collision per-tile (not per-elevation layer)
2. **Fix collision checking order**: Check collision override BEFORE elevation mismatch
3. **Add elevation special cases**: Handle elevation 0 (wildcard) and 15 (bridges)
4. **Update CollisionLayerCache**: Store both collision and elevation per-tile, provide `IsElevationMatch()` method
5. **Update MapConversionService**: Don't build separate layers per elevation - store per-tile data

## Impact on Existing Code

- **MapConversionService.BuildCollisionLayers()**: Needs to be removed or changed to store per-tile data
- **CollisionLayerCache**: Needs complete redesign to store per-tile collision and elevation
- **CollisionService.CanMoveTo()**: Needs to check collision before elevation, add elevation special cases
- **Design document**: Needs update to reflect per-tile storage and correct checking order

