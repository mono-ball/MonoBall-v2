# Collision System Architecture Analysis

## Executive Summary

This document analyzes the collision system design for architecture issues, Arch ECS/event system problems, SOLID/DRY violations, and potential bugs that could affect future updates and modding.

**Critical Issues Found**: 23 issues identified across architecture, ECS patterns, SOLID principles, and modding safety.

---

## 1. Architecture Issues

### Issue 1.1: CollisionService Not an ECS System

**Problem**: `CollisionService` is a service, not an ECS system, but it needs access to `World` for entity queries (`GetEntityElevation()`).

**Current Design**:
```csharp
public class CollisionService : ICollisionService
{
    private byte GetEntityElevation(Entity entity)
    {
        if (World.TryGet<PlayerComponent>(entity, out _))  // ❌ World not available
            return (byte)_constants.Get<int>("PlayerElevation");
        // ...
    }
}
```

**Impact**: 
- Services shouldn't directly access `World` (violates separation of concerns)
- Makes testing harder (need to mock World)
- Creates tight coupling to ECS

**Solution**: 
- Option A: Pass elevation as parameter (caller gets it)
- Option B: Inject `IEntityElevationService` that wraps World access
- Option C: Make CollisionService a system (but then it's not on-demand)

**Recommendation**: Option B - Create `IEntityElevationService` interface.

### Issue 1.2: Inconsistent Data Structure Documentation

**Problem**: Design document shows old per-elevation layer structure in `CollisionLayerCache` example (lines 138-165), but later says per-tile storage.

**Current Design**:
```csharp
// Lines 138-165: Shows per-elevation layers
private readonly Dictionary<string, Dictionary<int, byte[]>> _collisionLayers;

// But later says: "per-tile, not per-elevation layer"
```

**Impact**: Confusing for implementers, could lead to wrong implementation.

**Solution**: Update all examples to show per-tile storage consistently.

### Issue 1.3: Missing World Access Pattern

**Problem**: `CollisionService` needs World for `IsEntityBlocking()` check, but design doesn't specify how World is accessed.

**Current Design**:
```csharp
if (IsEntityBlocking(otherEntity))  // ❌ How does this access World?
    return false;
```

**Impact**: Implementation ambiguity, potential architecture violations.

**Solution**: Specify that `CollisionService` should inject `World` or use a service pattern.

### Issue 1.4: Constants Service Dependency

**Problem**: `GetEntityElevation()` uses `_constants.Get<int>("PlayerElevation")` but constants service isn't listed as a dependency.

**Current Design**:
```csharp
private byte GetEntityElevation(Entity entity)
{
    return (byte)_constants.Get<int>("PlayerElevation");  // ❌ _constants not defined
}
```

**Impact**: Missing dependency, unclear how constants are accessed.

**Solution**: Add `IConstantsService` to dependency list, or use component-based elevation.

---

## 2. Arch ECS Issues

### Issue 2.1: Entity Elevation Not a Component

**Problem**: Elevation is retrieved via `GetEntityElevation()` helper, but it's not stored as a component. Player elevation comes from constants, NPCs from `NpcComponent`.

**Current Design**:
```csharp
// Player: from constants
if (World.TryGet<PlayerComponent>(entity, out _))
    return (byte)_constants.Get<int>("PlayerElevation");

// NPC: from NpcComponent.Elevation
if (World.TryGet<NpcComponent>(entity, out var npc))
    return (byte)npc.Elevation;
```

**Impact**: 
- Inconsistent data storage (constants vs component)
- Harder to query entities by elevation
- Violates ECS principle: data should be in components

**Solution**: Create `ElevationComponent` for all entities:
```csharp
public struct ElevationComponent
{
    public byte Elevation { get; set; }
}
```

**Benefits**:
- Consistent data storage
- Can query entities by elevation: `QueryDescription().WithAll<ElevationComponent>()`
- Easier to modify elevation dynamically

### Issue 2.2: ReadOnlySpan Return Type May Be Invalid

**Problem**: `IEntityPositionService.GetEntitiesAt()` returns `ReadOnlySpan<Entity>`, but entities are stored in collections that may be modified.

**Current Design**:
```csharp
ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y, int elevation);
```

**Impact**: 
- `ReadOnlySpan` requires contiguous memory
- If spatial hash uses `List<Entity>`, span may become invalid if list is modified
- Could cause memory safety issues

**Solution**: 
- Option A: Return `IReadOnlyList<Entity>` (safer, but allocation)
- Option B: Return `ReadOnlySpan<Entity>` but document that it's only valid until next frame
- Option C: Use `ArrayPool<Entity>` for zero-allocation spans

**Recommendation**: Option B with clear documentation, or Option C for zero-allocation.

### Issue 2.3: Entity Comparison Without Validation

**Problem**: `otherEntity == entity` comparison doesn't check if entities are still alive.

**Current Design**:
```csharp
foreach (var otherEntity in entitiesAtPosition)
{
    if (otherEntity == entity)
        continue; // Skip self
    
    if (IsEntityBlocking(otherEntity))  // ❌ Entity might be destroyed
        return false;
}
```

**Impact**: Could check collision on destroyed entities, causing exceptions.

**Solution**: Add `World.IsAlive(otherEntity)` check:
```csharp
if (otherEntity == entity || !World.IsAlive(otherEntity))
    continue;
```

### Issue 2.4: Missing Query Caching

**Problem**: Design mentions "Cache QueryDescription" but doesn't show how `CollisionService` would cache queries if it needs to query entities.

**Current Design**: No query caching shown for entity blocking checks.

**Impact**: If `IsEntityBlocking()` uses queries, they should be cached per rules.

**Solution**: If queries are needed, cache them as instance fields.

---

## 3. Event System Issues

### Issue 3.1: Event Fired Too Early

**Problem**: `CollisionCheckEvent` is fired before bounds check, but event handlers might assume bounds are valid.

**Current Design**:
```csharp
// 2. Bounds check
if (!_collisionLayerCache.IsInBounds(mapId, targetX, targetY))
    return false;

// 4. Fire collision check event
var collisionCheckEvent = new CollisionCheckEvent { ... };
EventBus.Send(ref collisionCheckEvent);
```

**Impact**: 
- Event handlers might try to access tile data for out-of-bounds positions
- Inconsistent: bounds checked but event still fires for invalid positions

**Solution**: Fire event AFTER bounds check, or document that handlers must check bounds.

**Recommendation**: Fire event after bounds check (current order is fine, but document it).

### Issue 3.2: Event Can Be Modified After Check

**Problem**: `CollisionCheckEvent` is a `struct` passed by `ref`, but after checking `IsBlocked`, the event could be modified by other handlers.

**Current Design**:
```csharp
EventBus.Send(ref collisionCheckEvent);
if (collisionCheckEvent.IsBlocked)
    return false; // ✅ Checked
    
// But what if another handler modifies it after this?
```

**Impact**: Low (event is passed by ref, modifications are immediate), but could be confusing.

**Solution**: Document that event modifications are immediate and checked immediately after `Send()`.

### Issue 3.3: Missing Event Priority Documentation

**Problem**: Design doesn't specify event priority for `CollisionCheckEvent` handlers.

**Current Design**: No priority mentioned.

**Impact**: 
- Scripts might override core collision logic unintentionally
- No way to ensure core systems run before mods

**Solution**: Document priority levels:
- Priority 1000+: Core collision checks (run first)
- Priority 500: Normal tile interaction scripts
- Priority 0: Mods and effects

### Issue 3.4: Event Doesn't Include Current Position

**Problem**: `CollisionCheckEvent` has `TargetPosition` but not current position, making it hard for scripts to calculate movement delta.

**Current Design**:
```csharp
public struct CollisionCheckEvent
{
    public (int X, int Y) TargetPosition { get; set; }
    // ❌ Missing: CurrentPosition
}
```

**Impact**: Scripts need to query entity position separately to know where movement starts.

**Solution**: Add `CurrentPosition` property:
```csharp
public (int X, int Y) CurrentPosition { get; set; }
```

### Issue 3.5: No Event for Successful Collision Check

**Problem**: Only `CollisionCheckEvent` (before) and `CollisionDetectedEvent` (on collision) exist. No event for successful movement validation.

**Current Design**: Missing event for "collision check passed, movement allowed".

**Impact**: 
- Scripts can't react to successful collision checks
- Can't track which tiles are walkable for pathfinding mods

**Solution**: Add optional `CollisionCheckPassedEvent` (informational, not cancellable).

---

## 4. SOLID Principle Violations

### Issue 4.1: Single Responsibility Violation

**Problem**: `CollisionService` does too much:
- Bounds checking
- Elevation matching
- Entity collision
- Event firing
- Tile collision override checking

**Current Design**: One service handles all collision logic.

**Impact**: Hard to test, hard to extend, violates SRP.

**Solution**: Split into smaller services:
- `IBoundsService` - Bounds checking
- `IElevationService` - Elevation matching
- `IEntityCollisionService` - Entity collision
- `ICollisionService` - Orchestrates others

**Recommendation**: Keep as-is for now (YAGNI), but document that it could be split later.

### Issue 4.2: Open/Closed Violation

**Problem**: Adding new collision types requires modifying `CollisionService.CanMoveTo()`.

**Current Design**: Hardcoded collision checks in one method.

**Impact**: Can't extend collision system without modifying core code.

**Solution**: Use strategy pattern or plugin system:
```csharp
public interface ICollisionCheckStrategy
{
    bool CheckCollision(CollisionCheckContext context);
}

// CollisionService uses list of strategies
private readonly List<ICollisionCheckStrategy> _strategies;
```

**Recommendation**: Keep as-is for now, but consider strategy pattern if many collision types are added.

### Issue 4.3: Dependency Inversion Violation

**Problem**: `CollisionService` depends on concrete `ICollisionLayerCache`, `IEntityPositionService`, etc., but design doesn't show interfaces for all dependencies.

**Current Design**: Some dependencies are interfaces, some might be concrete.

**Impact**: Hard to test, hard to swap implementations.

**Solution**: Ensure all dependencies are interfaces:
- ✅ `ICollisionLayerCache` (interface)
- ✅ `IEntityPositionService` (interface)
- ❓ `IConstantsService` (not shown, should be interface)
- ❓ `World` (should be wrapped in interface or service)

### Issue 4.4: Interface Segregation Violation

**Problem**: `ICollisionService` has two methods (`CanMoveTo()` and `GetTileCollisionInfo()`), but some callers might only need one.

**Current Design**:
```csharp
public interface ICollisionService
{
    bool CanMoveTo(...);
    (bool, Direction, bool) GetTileCollisionInfo(...);
}
```

**Impact**: Callers must depend on entire interface even if they only use one method.

**Solution**: Split into smaller interfaces:
```csharp
public interface IWalkabilityService
{
    bool CanMoveTo(...);
}

public interface ITileInfoService
{
    TileCollisionInfo GetTileCollisionInfo(...);
}
```

**Recommendation**: Keep as-is (interface is small enough), but consider splitting if it grows.

---

## 5. DRY Violations

### Issue 5.1: Elevation Logic Duplicated

**Problem**: Elevation matching logic (`IsElevationMatch()`) is in `ICollisionLayerCache`, but `GetEntityElevation()` logic is in `CollisionService`.

**Current Design**: Elevation logic split across multiple places.

**Impact**: If elevation rules change, need to update multiple places.

**Solution**: Centralize elevation logic in `IElevationService`:
```csharp
public interface IElevationService
{
    byte GetEntityElevation(Entity entity);
    bool IsElevationMatch(byte entityElevation, byte tileElevation);
    byte? GetTileElevation(string mapId, int x, int y);
}
```

### Issue 5.2: Position Validation Duplicated

**Problem**: Bounds checking might be duplicated in `CollisionService` and `ICollisionLayerCache`.

**Current Design**: `CollisionService` calls `_collisionLayerCache.IsInBounds()`, but might also validate elsewhere.

**Impact**: Duplicate validation logic.

**Solution**: Ensure bounds checking is only in `ICollisionLayerCache`, document that callers should check bounds first.

### Issue 5.3: Entity Blocking Logic Not Reusable

**Problem**: `IsEntityBlocking()` logic is in `CollisionService`, but other systems might need to check if entities block.

**Current Design**: Logic is private to `CollisionService`.

**Impact**: Can't reuse entity blocking logic elsewhere.

**Solution**: Extract to `IEntityBlockingService`:
```csharp
public interface IEntityBlockingService
{
    bool IsEntityBlocking(Entity entity);
    bool CanEntitiesCollide(Entity a, Entity b);
}
```

---

## 6. Potential Bugs for Future Updates

### Issue 6.1: Elevation Type Mismatch

**Problem**: `CollisionCheckEvent.Elevation` is `int`, but `GetEntityElevation()` returns `byte`.

**Current Design**:
```csharp
public struct CollisionCheckEvent
{
    public int Elevation { get; set; }  // ❌ int
}

private byte GetEntityElevation(Entity entity)  // ✅ byte
{
    return (byte)_constants.Get<int>("PlayerElevation");
}
```

**Impact**: Type mismatch, potential overflow if elevation > 255 (shouldn't happen, but inconsistent).

**Solution**: Use `byte` consistently:
```csharp
public byte Elevation { get; set; }
```

### Issue 6.2: Null MapId Handling

**Problem**: `mapId` can be `null`, but design doesn't specify what happens if `mapId` is null in cache lookups.

**Current Design**:
```csharp
if (mapId == null)
    return false; // ✅ Handled

// But later:
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
// ❌ What if mapId is null here? (shouldn't happen, but not validated)
```

**Impact**: Potential null reference exceptions if code path changes.

**Solution**: Add null checks or use nullable-aware code:
```csharp
if (mapId == null)
    return false;
    
// Now mapId is known to be non-null
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId!, targetX, targetY);
```

### Issue 6.3: Elevation Special Cases Not Documented in Interface

**Problem**: `IsElevationMatch()` handles special cases (0, 15), but interface doesn't document this.

**Current Design**: Special cases are in implementation, not interface documentation.

**Impact**: Implementers might not handle special cases correctly.

**Solution**: Document in interface:
```csharp
/// <summary>
/// Checks if entity elevation matches tile elevation.
/// Special cases:
/// - Entity elevation 0 = wildcard (matches any tile elevation)
/// - Tile elevation 0 or 15 = no mismatch (special cases)
/// - Otherwise, must match exactly.
/// </summary>
bool IsElevationMatch(byte entityElevation, byte tileElevation);
```

### Issue 6.4: Collision Value 0 vs Null Ambiguity

**Problem**: `GetCollisionValue()` returns `byte?` (nullable), but 0 is a valid collision value (passable). Null might mean "out of bounds" or "no data".

**Current Design**:
```csharp
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
if (collisionValue.HasValue && collisionValue.Value > 0)
    return false;
```

**Impact**: 
- Null = out of bounds? Or no collision data?
- 0 = passable
- Ambiguity could cause bugs

**Solution**: Document clearly:
```csharp
/// <returns>
/// - null: Position is out of bounds
/// - 0: Passable (no collision)
/// - 1-3: Blocked (collision override)
/// </returns>
byte? GetCollisionValue(string mapId, int x, int y);
```

### Issue 6.5: Event Modification Race Condition

**Problem**: Multiple handlers can modify `CollisionCheckEvent.IsBlocked`, but order is undefined.

**Current Design**:
```csharp
EventBus.Send(ref collisionCheckEvent);
if (collisionCheckEvent.IsBlocked)
    return false;
```

**Impact**: 
- Handler A sets `IsBlocked = false`
- Handler B sets `IsBlocked = true`
- Result depends on handler order (undefined)

**Solution**: Document that handlers should check current state:
```csharp
On<CollisionCheckEvent>(evt =>
{
    if (evt.IsBlocked)  // Check if already blocked
        return;  // Don't override
    
    // Only block if not already blocked
    if (ShouldBlock(evt))
        evt.IsBlocked = true;
});
```

Or use priority system to ensure order.

---

## 7. Modding Safety Issues

### Issue 7.1: Scripts Can Bypass All Collision

**Problem**: Scripts can set `CollisionCheckEvent.IsBlocked = false` to bypass ALL collision checks (tile, elevation, entity).

**Current Design**:
```csharp
EventBus.Send(ref collisionCheckEvent);
if (collisionCheckEvent.IsBlocked)
    return false;  // ✅ Blocks movement

// But scripts can set IsBlocked = false to allow movement
```

**Impact**: 
- Mods can create cheats (walk through walls)
- No way to prevent mods from overriding core collision

**Solution**: 
- Option A: Document that mods can override (intended behavior)
- Option B: Add "core collision" flag that can't be overridden
- Option C: Fire event AFTER core checks, mods can only add restrictions

**Recommendation**: Option A (document as intended), but consider Option C for security.

### Issue 7.2: No Way to Query Collision Without Side Effects

**Problem**: `CanMoveTo()` fires events, so querying collision has side effects (events fire, scripts might react).

**Current Design**: Every collision check fires events.

**Impact**: 
- Pathfinding mods would fire many events
- Performance impact
- Unintended script reactions

**Solution**: Add `CanMoveToSilent()` method that doesn't fire events:
```csharp
bool CanMoveToSilent(Entity entity, int targetX, int targetY, string? mapId, Direction fromDirection);
```

### Issue 7.3: Script Attachment Strategy Undefined

**Problem**: Design mentions "Option A: Attach to Tile Entities" but doesn't specify how scripts are attached or when they're cleaned up.

**Current Design**: Vague about script lifecycle.

**Impact**: 
- Scripts might not be cleaned up (memory leak)
- Scripts might not be attached correctly
- Hot-reload might break

**Solution**: Specify script lifecycle:
- Scripts attached when map loads
- Scripts disposed when map unloads
- Scripts can be hot-reloaded (re-attach on reload)

### Issue 7.4: No Script Context for Tile Position

**Problem**: Scripts need to know their tile position, but design doesn't specify how scripts get this information.

**Current Design**:
```csharp
var tilePos = GetTilePosition();  // ❌ Method not defined
```

**Impact**: Scripts can't determine which tile they're attached to.

**Solution**: Specify script context:
```csharp
// Script context includes tile position
public class TileScriptContext : ScriptContext
{
    public (int X, int Y) TilePosition { get; }
    public int Elevation { get; }
    public string MapId { get; }
}
```

---

## 8. Performance Issues

### Issue 8.1: Event Allocation Per Collision Check

**Problem**: `CollisionCheckEvent` is allocated on every collision check (frequent operation).

**Current Design**:
```csharp
var collisionCheckEvent = new CollisionCheckEvent { ... };  // Allocation
EventBus.Send(ref collisionCheckEvent);
```

**Impact**: GC pressure, performance impact.

**Solution**: Use event pooling or make event allocation optional:
```csharp
// Only fire event if there are subscribers
if (EventBus.HasSubscribers<CollisionCheckEvent>())
{
    var evt = EventPool<CollisionCheckEvent>.Get();
    // ... populate event
    EventBus.Send(ref evt);
    EventPool<CollisionCheckEvent>.Return(evt);
}
```

### Issue 8.2: Multiple Cache Lookups

**Problem**: `GetCollisionValue()` and `GetElevation()` are separate calls, might do duplicate bounds checking.

**Current Design**:
```csharp
byte? collisionValue = _collisionLayerCache.GetCollisionValue(mapId, targetX, targetY);
byte? tileElevation = _collisionLayerCache.GetElevation(mapId, targetX, targetY);
```

**Impact**: Two cache lookups instead of one.

**Solution**: Add combined method:
```csharp
(CollisionValue: byte?, Elevation: byte?) GetTileData(string mapId, int x, int y);
```

---

## Summary of Critical Issues

### High Priority (Must Fix)

1. **Issue 1.1**: CollisionService needs World access pattern
2. **Issue 2.1**: Entity elevation should be a component
3. **Issue 6.1**: Elevation type mismatch (int vs byte)
4. **Issue 6.4**: Collision value null vs 0 ambiguity

### Medium Priority (Should Fix)

5. **Issue 3.4**: Event missing current position
6. **Issue 4.3**: Dependency inversion (ensure all dependencies are interfaces)
7. **Issue 5.1**: Elevation logic duplication
8. **Issue 7.2**: No silent collision query method

### Low Priority (Nice to Have)

9. **Issue 4.1**: Single responsibility (split services)
10. **Issue 8.1**: Event allocation optimization
11. **Issue 8.2**: Multiple cache lookups

---

## Recommendations

1. **Create `ElevationComponent`** for all entities (Issue 2.1)
2. **Create `IEntityElevationService`** to wrap World access (Issue 1.1)
3. **Use `byte` consistently** for elevation (Issue 6.1)
4. **Document null/0 ambiguity** clearly (Issue 6.4)
5. **Add `CurrentPosition` to event** (Issue 3.4)
6. **Consider event pooling** for performance (Issue 8.1)
7. **Document script lifecycle** clearly (Issue 7.3)

