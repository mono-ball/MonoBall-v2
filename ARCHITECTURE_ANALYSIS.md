# Architecture & Arch ECS Analysis - MovementSystem & MapLoaderSystem Changes

## Summary
Analysis of code changes made to add GridMovement to NPCs and optimize MovementSystem for performance.

---

## 🔴 Critical Issues

### 1. **Performance: Inefficient Component Access in Hot Path**
**Location:** `MovementSystem.cs:262-263`

```csharp
bool isPlayer = World.Has<PlayerComponent>(entity);
bool hasMovementRequest = World.Has<MovementRequest>(entity);
```

**Problem:**
- `World.Has<>()` requires two lookups: one to check existence, one to access
- Called for every entity in the query, even ones we skip
- Should use `TryGet<>()` which is more efficient

**Fix:**
```csharp
bool isPlayer = World.TryGet<PlayerComponent>(entity, out _);
bool hasMovementRequest = World.TryGet<MovementRequest>(entity, out _);
```

---

### 2. **Performance: Active Map IDs Recalculated Every Frame**
**Location:** `MovementSystem.cs:510-524`

```csharp
private HashSet<string> GetActiveMapIds()
{
    HashSet<string> activeMapIds = new HashSet<string>();
    World.Query(in _mapQuery, (Entity entity, ref MapComponent map) =>
    {
        activeMapIds.Add(map.MapId);
    });
    return activeMapIds;
}
```

**Problem:**
- Creates new HashSet every frame (allocation)
- Queries all map entities every frame
- Maps don't change frequently - should be cached

**Impact:** 
- ~0.1-0.5ms per frame (depending on map count)
- GC pressure from HashSet allocations

**Fix:** Cache with event-based invalidation (see recommendations)

---

### 3. **Performance: Inefficient GetMapId Pattern**
**Location:** `MovementSystem.cs:495-503`

```csharp
private string? GetMapId(Entity entity)
{
    if (World.Has<MapComponent>(entity))
    {
        ref var mapComponent = ref World.Get<MapComponent>(entity);
        return mapComponent.MapId;
    }
    return null;
}
```

**Problem:**
- Uses `Has<>()` + `Get<>()` pattern (two lookups)
- Should use `TryGet<>()` (single lookup)

**Fix:**
```csharp
private string? GetMapId(Entity entity)
{
    if (World.TryGet<MapComponent>(entity, out var mapComponent))
    {
        return mapComponent.MapId;
    }
    return null;
}
```

---

## 🟡 Architecture Issues

### 4. **Separation of Concerns: Map Filtering Logic in MovementSystem**
**Location:** `MovementSystem.cs:505-559`

**Problem:**
- MovementSystem contains map filtering logic
- This is map lifecycle management, not movement logic
- Violates Single Responsibility Principle

**Recommendation:**
- Extract to `IMapFilterService` or similar
- MovementSystem should focus on movement, not map management
- Could subscribe to `MapLoadedEvent` / `MapUnloadedEvent` for cache updates

---

### 5. **Missing Event-Based Cache Invalidation**
**Location:** `MovementSystem.cs` (missing)

**Problem:**
- Active map IDs cache (if implemented) has no invalidation mechanism
- Maps can be loaded/unloaded without MovementSystem knowing
- Cache could become stale

**Recommendation:**
- Subscribe to `MapLoadedEvent` and `MapUnloadedEvent`
- Invalidate cache when maps change
- Or inject `MapLoaderSystem` to query directly (creates coupling)

---

## 🟢 Arch ECS Best Practices

### ✅ Good Practices (Already Implemented)

1. **Query Caching:** QueryDescriptions are cached as instance fields ✓
2. **Query Reuse:** Queries are created once in constructor ✓
3. **Component Access Order:** `IsEntityInActiveMaps` checks components in order of likelihood ✓
4. **TryGet Pattern:** Used in `IsEntityInActiveMaps` for efficient component access ✓

### ✅ Query Iteration Issue - RESOLVED

1. **Query Iteration Overhead - FIXED:**
   - **Before:** Arch ECS queries iterate over ALL matching entities, even with early returns in lambda
   - **After:** Use `ActiveMapEntity` tag component in QueryDescription to filter at query level
   - **Solution:** Include `ActiveMapEntity` in queries - Arch ECS only iterates over entities with ALL required components
   - **Result:** No iteration over entities in unloaded maps - query-level filtering eliminates the overhead

2. **Spatial Filtering in Query - RESOLVED:**
   - **Before:** Can't filter by map ID directly in QueryDescription
   - **After:** Use `ActiveMapEntity` tag component as a proxy for "in active maps"
   - **Result:** Query-level filtering achieved through component tagging pattern
   - **Example:**
     ```csharp
     // Query only NPCs in active maps - Arch ECS filters at query level
     _npcQuery = new QueryDescription().WithAll<
         NpcComponent,
         SpriteAnimationComponent,
         ActiveMapEntity  // Arch ECS only iterates entities with this tag
     >();
     ```

---

## 📋 Recommended Fixes (Priority Order)

### Priority 1: Hot Path Optimizations

1. **Fix component access in UpdateMovements:**
   ```csharp
   // Change from:
   bool isPlayer = World.Has<PlayerComponent>(entity);
   bool hasMovementRequest = World.Has<MovementRequest>(entity);
   
   // To:
   bool isPlayer = World.TryGet<PlayerComponent>(entity, out _);
   bool hasMovementRequest = World.TryGet<MovementRequest>(entity, out _);
   ```

2. **Fix GetMapId:**
   ```csharp
   // Change from Has<> + Get<> to TryGet<>
   private string? GetMapId(Entity entity)
   {
       if (World.TryGet<MapComponent>(entity, out var mapComponent))
       {
           return mapComponent.MapId;
       }
       return null;
   }
   ```

### Priority 2: Cache Active Map IDs

**Option A: Simple Cache with Manual Invalidation**
```csharp
private HashSet<string>? _cachedActiveMapIds;
private int _lastMapEntityCount = -1;

private HashSet<string> GetActiveMapIds()
{
    // Quick check: if map entity count hasn't changed, return cache
    int currentCount = World.CountEntities(in _mapQuery);
    if (_cachedActiveMapIds != null && currentCount == _lastMapEntityCount)
    {
        return _cachedActiveMapIds;
    }
    
    // Rebuild cache
    HashSet<string> activeMapIds = new HashSet<string>();
    World.Query(in _mapQuery, (Entity entity, ref MapComponent map) =>
    {
        activeMapIds.Add(map.MapId);
    });
    
    _cachedActiveMapIds = activeMapIds;
    _lastMapEntityCount = currentCount;
    return activeMapIds;
}
```

**Option B: Event-Based Cache (Better Architecture)**
```csharp
private HashSet<string> _cachedActiveMapIds = new HashSet<string>();
private bool _cacheInvalidated = true;

public MovementSystem(...) : base(world)
{
    // ... existing code ...
    EventBus.Subscribe<MapLoadedEvent>(OnMapLoaded);
    EventBus.Subscribe<MapUnloadedEvent>(OnMapUnloaded);
}

private void OnMapLoaded(ref MapLoadedEvent evt)
{
    _cachedActiveMapIds.Add(evt.MapId);
    _cacheInvalidated = false;
}

private void OnMapUnloaded(ref MapUnloadedEvent evt)
{
    _cachedActiveMapIds.Remove(evt.MapId);
    _cacheInvalidated = false;
}

private HashSet<string> GetActiveMapIds()
{
    if (_cacheInvalidated)
    {
        // Rebuild from scratch
        _cachedActiveMapIds.Clear();
        World.Query(in _mapQuery, (Entity entity, ref MapComponent map) =>
        {
            _cachedActiveMapIds.Add(map.MapId);
        });
        _cacheInvalidated = false;
    }
    return _cachedActiveMapIds;
}
```

### Priority 3: Extract Map Filtering Service (Optional)

Create `IMapFilterService`:
```csharp
public interface IMapFilterService
{
    bool IsEntityInActiveMaps(Entity entity);
    HashSet<string> GetActiveMapIds();
}
```

This would:
- Separate concerns (map management vs movement)
- Make MovementSystem more testable
- Allow reuse by other systems

---

## 🔍 Code Quality Issues

### 6. **Unused Import**
**Location:** `MovementSystem.cs:3`

```csharp
using System.Linq;  // Not used anywhere
```

**Fix:** Remove unused import

---

## 📊 Performance Impact Estimates

| Issue | Current Cost | After Fix | Savings |
|-------|-------------|-----------|---------|
| Has<> calls (2 per entity) | ~0.05-0.1ms | ~0.02-0.04ms | 50-60% |
| GetActiveMapIds() per frame | ~0.1-0.5ms | ~0.001ms (cache hit) | 99% |
| GetMapId() pattern | ~0.01ms | ~0.005ms | 50% |

**Total Estimated Savings:** ~0.15-0.6ms per frame (depending on entity count)

---

## ✅ What's Working Well

1. **Query Structure:** Properly cached QueryDescriptions
2. **Early Returns:** Correctly skipping inactive entities
3. **Component Access Order:** Checking most likely components first
4. **Map Filtering:** Correctly filtering by active maps
5. **Stationary NPC Skip:** Good optimization to skip non-moving NPCs

---

## 🎯 Action Items

1. ✅ **DONE:** Add GridMovement to all NPCs
2. ✅ **DONE:** Filter by active maps
3. ✅ **DONE:** Skip stationary NPCs
4. ✅ **FIXED:** Fix `World.Has<>` to `TryGet<>` in hot path
5. ✅ **FIXED:** Cache active map IDs
6. ✅ **FIXED:** Fix `GetMapId` pattern
7. ⚠️ **OPTIONAL:** Extract map filtering to service (architectural improvement, not critical)
