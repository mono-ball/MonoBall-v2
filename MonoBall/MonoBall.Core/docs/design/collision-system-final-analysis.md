# Collision System Design - Final Architecture & Performance Analysis

## Executive Summary

After removing the elevation 15 special case and implementing natural elevation changes, this document provides a final analysis of architecture and performance issues in the collision system design.

**Issues Found**: 12 architecture issues, 8 performance issues

---

## 1. Architecture Issues

### Issue 1.1: Redundant Cache Lookups

**Problem**: `GetCollisionValue()` and `GetElevation()` are separate calls, requiring two cache lookups and two bounds checks.

**Current Design**:
```csharp
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
// ... later ...
byte? tileElevation = _collisionLayerCache.GetElevation(mapId, targetX, targetY);
```

**Impact**: 
- Two cache dictionary lookups
- Two bounds checks (if implemented in each method)
- Inefficient for hot path

**Solution**: Add combined method:
```csharp
public interface ICollisionLayerCache
{
    /// <summary>
    /// Gets both collision value and elevation in a single call.
    /// More efficient than separate calls.
    /// </summary>
    (CollisionValue: byte?, Elevation: byte?) GetTileData(string mapId, int x, int y);
}
```

### Issue 1.2: Elevation Mismatch Logic Bug

**Problem**: Elevation mismatch check condition is incorrect - it checks `collisionValue.HasValue && collisionValue.Value > 0`, but `collisionValue` might be null if we already returned false.

**Current Design**:
```csharp
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
if (collisionValue.HasValue && collisionValue.Value > 0)
{
    return false; // Blocked by collision override
}

// Later...
if (tileElevation.HasValue && collisionValue.HasValue && collisionValue.Value > 0)
{
    // ❌ collisionValue might be null here if we didn't return false above
}
```

**Impact**: Logic bug - if collision override = 0, `collisionValue` is still set, but the condition is confusing.

**Solution**: Store collision value in a local variable and reuse:
```csharp
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
bool isSolid = collisionValue.HasValue && collisionValue.Value > 0;

if (isSolid)
{
    return false; // Blocked by collision override
}

// Later...
if (tileElevation.HasValue && isSolid)  // ✅ Clear condition
{
    // Check elevation mismatch
}
```

**Better Solution**: Only check elevation mismatch if solid:
```csharp
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
bool isSolid = collisionValue.HasValue && collisionValue.Value > 0;

if (isSolid)
{
    // Check elevation mismatch only for solid tiles
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
```

### Issue 1.3: Missing CanMoveToSilent Implementation

**Problem**: Design mentions `CanMoveToSilent()` but doesn't show implementation.

**Current Design**: Only `CanMoveTo()` is shown.

**Impact**: Pathfinding algorithms can't use silent collision checks.

**Solution**: Add implementation:
```csharp
public bool CanMoveToSilent(
    Entity entity,
    int targetX,
    int targetY,
    string? mapId,
    Direction fromDirection = Direction.None
)
{
    // Same logic as CanMoveTo(), but skip event firing
    // Steps 1-4, 6-8 (skip step 5 - event firing)
}
```

### Issue 1.4: Entity Elevation Update Not Handled

**Problem**: Design mentions "natural elevation changes" but doesn't specify when/how entity elevation is updated.

**Current Design**: `CanMoveTo()` checks elevation but doesn't update entity elevation when movement succeeds.

**Impact**: 
- Entity elevation won't change when moving to different elevation tiles
- MovementSystem needs to handle elevation updates separately

**Solution**: Either:
- Option A: `CanMoveTo()` returns elevation change info
- Option B: MovementSystem updates elevation after successful movement
- Option C: Separate `UpdateEntityElevation()` method

**Recommendation**: Option B - MovementSystem handles elevation updates after movement completes (cleaner separation of concerns).

### Issue 1.5: TileInteractionCache Still Uses Elevation Parameter

**Problem**: `TileInteractionCache.GetTileInteractionId()` takes `elevation` parameter, but tiles are stored per-tile (not per-elevation layer).

**Current Design**:
```csharp
string? GetTileInteractionId(string mapId, int x, int y, int elevation);
```

**Issue**: Why does it need elevation? Tiles are stored per-tile, not per-elevation.

**Solution**: Remove elevation parameter:
```csharp
string? GetTileInteractionId(string mapId, int x, int y);
```

**Note**: If multiple layers can have tiles at same position with different elevations, we might need elevation. But design says per-tile storage, so elevation shouldn't be needed.

### Issue 1.6: IConstantsService Dependency Not Used

**Problem**: `CollisionService` takes `IConstantsService` but doesn't use it (elevation comes from `IEntityElevationService`).

**Current Design**:
```csharp
private readonly IConstantsService _constantsService;

public CollisionService(
    // ...
    IConstantsService constantsService
)
{
    _constantsService = constantsService;  // ❌ Never used
}
```

**Impact**: Unnecessary dependency.

**Solution**: Remove `IConstantsService` dependency (elevation comes from `IEntityElevationService`).

### Issue 1.7: Missing Elevation Update in MovementSystem

**Problem**: Design mentions natural elevation changes but doesn't show how MovementSystem updates entity elevation.

**Current Design**: MovementSystem calls `CanMoveTo()` but doesn't update elevation.

**Impact**: Entities won't change elevation when moving to different elevation tiles.

**Solution**: Add elevation update logic to MovementSystem:
```csharp
// After successful movement
var tileElevation = _collisionLayerCache.GetElevation(mapId, targetX, targetY);
if (tileElevation.HasValue)
{
    _elevationService.SetEntityElevation(entity, tileElevation.Value);
}
```

### Issue 1.8: ReadOnlySpan Lifetime Not Documented in Interface

**Problem**: `IEntityPositionService.GetEntitiesAt()` returns `ReadOnlySpan<Entity>`, but interface doesn't document lifetime.

**Current Design**: Only documented in implementation comments.

**Impact**: Callers might use span after it's invalid.

**Solution**: Document in interface:
```csharp
/// <summary>
/// Gets all entities at a specific tile position with matching elevation.
/// </summary>
/// <remarks>
/// WARNING: The returned ReadOnlySpan is only valid until the next frame update.
/// Do not store or use the span after the current method returns.
/// </remarks>
ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y, int elevation);
```

### Issue 1.9: Elevation Mismatch Check Order Issue

**Problem**: Elevation mismatch is checked AFTER collision override, but the condition checks `collisionValue` which might be confusing.

**Current Design**:
```csharp
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
if (collisionValue.HasValue && collisionValue.Value > 0)
{
    return false; // Blocked
}

// Later...
if (tileElevation.HasValue && collisionValue.HasValue && collisionValue.Value > 0)
{
    // Check elevation mismatch
}
```

**Issue**: If collision override = 0, we skip elevation check. But the condition `collisionValue.Value > 0` will be false, so elevation check is skipped. This is correct, but the logic flow is confusing.

**Solution**: Restructure for clarity:
```csharp
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
bool isSolid = collisionValue.HasValue && collisionValue.Value > 0;

if (isSolid)
{
    // Solid tile - check elevation mismatch
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
```

### Issue 1.10: Missing ElevationComponent Update Logic

**Problem**: Design recommends `ElevationComponent` but doesn't show how to update it when elevation changes.

**Current Design**: No elevation update logic shown.

**Impact**: Entities won't have their elevation updated when moving to different elevation tiles.

**Solution**: Add to MovementSystem or create elevation update system:
```csharp
// In MovementSystem, after successful movement
if (_elevationService.TryGetElevationComponent(entity, out var elevationComp))
{
    var tileElevation = _collisionLayerCache.GetElevation(mapId, targetX, targetY);
    if (tileElevation.HasValue)
    {
        elevationComp.Elevation = tileElevation.Value;
        World.Set(entity, elevationComp);
    }
}
```

### Issue 1.11: Event Allocation Per Collision Check

**Problem**: `CollisionCheckEvent` is allocated on every collision check (frequent operation).

**Current Design**:
```csharp
var collisionCheckEvent = new CollisionCheckEvent { ... };  // Allocation
EventBus.Send(ref collisionCheckEvent);
```

**Impact**: GC pressure, performance impact.

**Solution**: 
- Option A: Check if subscribers exist before allocating
- Option B: Use event pooling (if EventBus supports it)
- Option C: Make event allocation optional

**Recommendation**: Option A - check subscribers first:
```csharp
if (EventBus.HasSubscribers<CollisionCheckEvent>())
{
    var collisionCheckEvent = new CollisionCheckEvent { ... };
    EventBus.Send(ref collisionCheckEvent);
    if (collisionCheckEvent.IsBlocked)
        return false;
}
```

### Issue 1.12: Missing Validation for Elevation Changes

**Problem**: Design doesn't validate that elevation changes are reasonable (e.g., can't jump from elevation 3 to elevation 15 in one step).

**Current Design**: No validation for elevation delta.

**Impact**: Entities might teleport between elevations unexpectedly.

**Solution**: Add validation (optional, can be handled by scripts):
```csharp
// In MovementSystem, before updating elevation
var currentElevation = _elevationService.GetEntityElevation(entity);
var tileElevation = _collisionLayerCache.GetElevation(mapId, targetX, targetY);

if (tileElevation.HasValue)
{
    var elevationDelta = Math.Abs(tileElevation.Value - currentElevation);
    if (elevationDelta > MaxElevationChangePerStep)
    {
        // Too large elevation change - might be stairs/ramp, allow it
        // Or block if not on stairs/ramp (handled by scripts)
    }
}
```

**Recommendation**: Don't add this validation - let scripts handle it via `CollisionCheckEvent`.

---

## 2. Performance Issues

### Issue 2.1: Multiple Cache Lookups

**Problem**: `GetCollisionValue()` and `GetElevation()` are separate calls.

**Current Design**: Two separate cache lookups.

**Impact**: 
- Two dictionary lookups
- Two bounds checks
- Cache misses hit twice

**Solution**: Combined lookup method (see Issue 1.1).

**Performance Impact**: ~50% reduction in cache lookups.

### Issue 2.2: Event Allocation Per Check

**Problem**: `CollisionCheckEvent` allocated on every collision check.

**Current Design**: New struct allocation every call.

**Impact**: 
- GC pressure
- Allocation overhead

**Solution**: Check subscribers first (see Issue 1.11).

**Performance Impact**: Eliminates allocation when no subscribers (common case).

### Issue 2.3: Entity Position Query Every Check

**Problem**: `GetEntityPosition()` is called every collision check to populate event.

**Current Design**:
```csharp
var currentPos = _elevationService.GetEntityPosition(entity);
```

**Impact**: 
- ECS query or component lookup every check
- Unnecessary if no event subscribers

**Solution**: Only get position if subscribers exist:
```csharp
if (EventBus.HasSubscribers<CollisionCheckEvent>())
{
    var currentPos = _elevationService.GetEntityPosition(entity);
    // ... create event
}
```

**Performance Impact**: Eliminates unnecessary position query when no subscribers.

### Issue 2.4: Entity Elevation Query Every Check

**Problem**: `GetEntityElevation()` is called every collision check.

**Current Design**:
```csharp
byte entityElevation = _elevationService.GetEntityElevation(entity);
```

**Impact**: 
- ECS query or component lookup every check
- Could be cached if entity elevation doesn't change often

**Solution**: 
- Option A: Cache elevation in MovementSystem (entity elevation changes infrequently)
- Option B: Keep as-is (elevation might change during movement)

**Recommendation**: Keep as-is - elevation can change during movement (stairs, etc.).

### Issue 2.5: Spatial Hash Query Even When Not Needed

**Problem**: Entity collision check happens even if tile is already blocked.

**Current Design**: Entity check happens after tile checks, but still runs.

**Impact**: 
- Spatial hash lookup even when tile blocks movement
- Unnecessary work

**Solution**: Already optimized - entity check only happens if tile checks pass.

**Performance Impact**: Already optimal.

### Issue 2.6: ReadOnlySpan Iteration Overhead

**Problem**: Iterating over `ReadOnlySpan<Entity>` has overhead.

**Current Design**:
```csharp
foreach (var otherEntity in entitiesAtPosition)
{
    // Check each entity
}
```

**Impact**: 
- Span iteration overhead
- Multiple component lookups

**Solution**: Already optimal - span is efficient, component lookups are necessary.

**Performance Impact**: Already optimal.

### Issue 2.7: Missing Early Exit Optimization

**Problem**: Elevation mismatch check happens even if collision override already blocked.

**Current Design**: Elevation check happens after collision check, but condition is confusing.

**Impact**: Minor - logic is correct but could be clearer.

**Solution**: Restructure for clarity (see Issue 1.9).

**Performance Impact**: Negligible (already optimized).

### Issue 2.8: TileInteractionCache Not Used in Collision Check

**Problem**: Design shows `TileInteractionCache` but it's not used in `CanMoveTo()`.

**Current Design**: `TileInteractionCache` exists but collision check doesn't use it.

**Impact**: 
- Cache is built but not used
- Scripts handle interactions via events instead

**Solution**: This is correct - scripts handle interactions via events, cache is for other purposes (if needed).

**Performance Impact**: No issue - cache is optional.

---

## 3. Critical Issues Summary

### High Priority (Must Fix)

1. **Issue 1.2**: Elevation mismatch logic bug - condition is confusing
2. **Issue 1.4**: Missing elevation update logic - entities won't change elevation
3. **Issue 1.6**: Unused `IConstantsService` dependency
4. **Issue 1.7**: Missing elevation update in MovementSystem

### Medium Priority (Should Fix)

5. **Issue 1.1**: Redundant cache lookups - add combined method
6. **Issue 1.3**: Missing `CanMoveToSilent()` implementation
7. **Issue 1.8**: ReadOnlySpan lifetime documentation
8. **Issue 1.9**: Elevation mismatch check order clarity

### Low Priority (Nice to Have)

9. **Issue 1.5**: TileInteractionCache elevation parameter
10. **Issue 1.10**: ElevationComponent update logic
11. **Issue 1.11**: Event allocation optimization
12. **Issue 1.12**: Elevation change validation

---

## 4. Recommended Fixes

### Fix 1: Restructure Elevation Mismatch Logic

```csharp
// 6. Check tile collision override (bits 10-11 from map.bin)
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
bool isSolid = collisionValue.HasValue && collisionValue.Value > 0;

if (isSolid)
{
    // Solid tile - check elevation mismatch
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
// Movement allowed, elevation can change naturally
```

### Fix 2: Add Combined Cache Lookup

```csharp
public interface ICollisionLayerCache
{
    /// <summary>
    /// Gets both collision value and elevation in a single call.
    /// More efficient than separate calls for hot path collision checks.
    /// </summary>
    (CollisionValue: byte?, Elevation: byte?) GetTileData(string mapId, int x, int y);
}
```

### Fix 3: Add Elevation Update to MovementSystem

```csharp
// In MovementSystem, after successful movement
private void UpdateEntityElevation(Entity entity, string mapId, int x, int y)
{
    var tileElevation = _collisionLayerCache.GetElevation(mapId, x, y);
    if (tileElevation.HasValue)
    {
        // Update entity elevation to match tile elevation
        // This enables natural elevation changes via stairs, ramps, etc.
        _elevationService.SetEntityElevation(entity, tileElevation.Value);
    }
}
```

### Fix 4: Remove Unused Dependency

```csharp
public CollisionService(
    ICollisionLayerCache collisionLayerCache,
    IEntityPositionService entityPositionService,
    IEntityElevationService elevationService
    // ❌ Remove: IConstantsService constantsService
)
```

### Fix 5: Add CanMoveToSilent Implementation

```csharp
public bool CanMoveToSilent(
    Entity entity,
    int targetX,
    int targetY,
    string? mapId,
    Direction fromDirection = Direction.None
)
{
    // Same as CanMoveTo(), but skip event firing (steps 1-4, 6-8, skip 5)
    // Used for pathfinding algorithms that need many collision checks
}
```

---

## 5. Performance Optimizations

### Optimization 1: Check Subscribers Before Event Allocation

```csharp
// Only fire event if there are subscribers
if (EventBus.HasSubscribers<CollisionCheckEvent>())
{
    var currentPos = _elevationService.GetEntityPosition(entity);
    var collisionCheckEvent = new CollisionCheckEvent { ... };
    EventBus.Send(ref collisionCheckEvent);
    if (collisionCheckEvent.IsBlocked)
        return false;
}
```

**Impact**: Eliminates allocation and position query when no subscribers (common case).

### Optimization 2: Combined Cache Lookup

```csharp
var (collisionValue, tileElevation) = _collisionLayerCache.GetTileData(mapId, targetX, targetY);
```

**Impact**: ~50% reduction in cache lookups.

### Optimization 3: Early Exit for Solid Tiles

```csharp
bool isSolid = collisionValue.HasValue && collisionValue.Value > 0;
if (isSolid)
{
    // Check elevation, then return false
    // No need to check entity collision if tile blocks
}
```

**Impact**: Already implemented, but logic could be clearer.

---

## 6. Architecture Improvements

### Improvement 1: Clear Separation of Concerns

- **CollisionService**: Checks if movement is allowed
- **MovementSystem**: Handles movement execution and elevation updates
- **IEntityElevationService**: Manages entity elevation queries and updates

### Improvement 2: Natural Elevation Changes

- Entities update elevation when moving to different elevation tiles
- No special cases needed
- Stairs, ramps, bridges work naturally

### Improvement 3: Event-Driven Scripts

- Scripts control movement via events
- No hardcoded behavior checks
- Fully moddable

---

## Conclusion

The design is **mostly sound** but has several issues:

1. **Logic bug**: Elevation mismatch condition is confusing
2. **Missing feature**: Elevation update logic not shown
3. **Performance**: Multiple cache lookups, event allocation
4. **Unused dependency**: IConstantsService not needed

**Recommended Actions**:
1. Fix elevation mismatch logic (Issue 1.2)
2. Add elevation update logic to MovementSystem (Issue 1.7)
3. Remove unused IConstantsService dependency (Issue 1.6)
4. Add combined cache lookup method (Issue 1.1)
5. Add CanMoveToSilent implementation (Issue 1.3)
6. Optimize event allocation (Issue 1.11)

