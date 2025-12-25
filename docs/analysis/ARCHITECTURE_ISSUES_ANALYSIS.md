# Architecture Issues Analysis - Comprehensive Review

**Date:** 2025-01-XX  
**Scope:** All recent changes for architecture violations, ECS issues, SOLID/DRY violations, bugs, and hacky fixes  
**Status:** 🔴 **CRITICAL ISSUES FOUND**

---

## Executive Summary

This analysis identifies multiple violations of project rules, architectural issues, and hacky fixes that need to be addressed:

- **🔴 CRITICAL:** 3 violations of "NO BACKWARD COMPATIBILITY" rule
- **🔴 CRITICAL:** 3 violations of "NO FALLBACK CODE" rule  
- **🔴 CRITICAL:** 4 QueryDescription objects created in hot paths (Update/Render methods)
- **🔴 CRITICAL:** 1 event subscription memory leak (SystemManager)
- **🟡 MEDIUM:** 1 hacky timing workaround (multiple Task.Yield calls)
- **🟡 MEDIUM:** 1 unused fallback query (dead code)

---

## 🔴 CRITICAL: NO BACKWARD COMPATIBILITY Violations

### 1. MovementSystem - Fallback Query for Backward Compatibility

**Location:** `MonoBall.Core/ECS/Systems/MovementSystem.cs:100-101`

**Issue:**
```csharp
// Fallback query without ActiveMapEntity (for backwards compatibility, but shouldn't be needed)
_movementQuery = new QueryDescription().WithAll<PositionComponent, GridMovement>();
```

**Violation:** Explicitly violates "NO BACKWARD COMPATIBILITY" rule - code comment says "for backwards compatibility"

**Problem:**
- Query is created but **NEVER USED** (dead code)
- Violates project rule: "NEVER maintain backward compatibility - refactor APIs freely"
- Comment indicates uncertainty ("shouldn't be needed")

**Fix Required:**
- **Remove the fallback query entirely** - it's dead code
- If there are call sites that need it, update them to use `_movementQueryWithActiveMap` instead
- Remove backward compatibility comment

**Impact:** LOW (dead code, but violates rules)

---

## 🔴 CRITICAL: NO FALLBACK CODE Violations

### 1. MovementSystem - Fallback Query

**Location:** `MonoBall.Core/ECS/Systems/MovementSystem.cs:100-101`

**Issue:** Same as above - fallback query violates both rules.

**Fix:** Remove entirely.

---

### 2. MapPopupSystem - Fallback Scene Entity Cleanup

**Location:** `MonoBall.Core/ECS/Systems/MapPopupSystem.cs:266-272`

**Issue:**
```csharp
else if (_currentPopupSceneEntity.HasValue && World.IsAlive(_currentPopupSceneEntity.Value))
{
    // Fallback: destroy tracked scene entity
    _sceneSystem.DestroyScene(_currentPopupSceneEntity.Value);
}
```

**Violation:** Fallback code that silently handles edge cases instead of failing fast.

**Problem:**
- If `sceneEntityToDestroy` is null/invalid, falls back to tracked entity
- This masks bugs - if scene entity isn't properly stored in component, we should fail fast
- Violates "NEVER introduce fallback code - code should fail fast"

**Fix Required:**
- Remove fallback logic
- If `sceneEntityToDestroy` is null/invalid, throw `InvalidOperationException` with clear message
- Ensure `MapPopupComponent.SceneEntity` is always set correctly (fail fast if not)

**Impact:** MEDIUM (could mask bugs)

---

### 3. MonoBallGame - Fallback Background Color

**Location:** `MonoBall.Core/MonoBallGame.cs:371-372`

**Issue:**
```csharp
else
{
    // Fallback to black if no renderer available
    backgroundColor = Color.Black;
}
```

**Violation:** Fallback behavior when `systemManager` is null.

**Problem:**
- If `systemManager` is null during Draw(), we should fail fast, not silently use black
- This masks initialization bugs

**Fix Required:**
- Throw `InvalidOperationException` if `systemManager` is null during Draw()
- Or ensure `systemManager` is always initialized before Draw() is called

**Impact:** LOW (unlikely to occur, but violates rules)

---

## 🔴 CRITICAL: QueryDescription Created in Hot Paths

### 1. ShaderTemplateSystem - QueryDescription in ApplyTemplate()

**Location:** `MonoBall.Core/Rendering/ShaderTemplateSystem.cs:66`

**Issue:**
```csharp
// Find existing layer shader entities for this layer
var existingEntities = new List<Entity>();
var query = new QueryDescription().WithAll<RenderingShaderComponent>();  // ❌ Created in method
_world.Query(in query, ...);
```

**Violation:** QueryDescription created in method instead of cached.

**Fix Required:**
- Cache as `private static readonly QueryDescription _renderingShaderQuery = new QueryDescription().WithAll<RenderingShaderComponent>();`
- Use cached query in both `ApplyTemplate()` and `ClearLayer()`

**Impact:** MEDIUM (performance issue, allocation per call)

---

### 2. ShaderTemplateSystem - QueryDescription in ClearLayer()

**Location:** `MonoBall.Core/Rendering/ShaderTemplateSystem.cs:136`

**Issue:**
```csharp
// Query for all entities with RenderingShaderComponent
var query = new QueryDescription().WithAll<RenderingShaderComponent>();  // ❌ Created in method
```

**Violation:** Same as above - should use cached query.

**Fix:** Use same cached query as above.

---

### 3. PerformanceStatsSystem - QueryDescription in Update()

**Location:** `MonoBall.Core/ECS/Systems/PerformanceStatsSystem.cs:115`

**Issue:**
```csharp
public override void Update(in float deltaTime)
{
    // ...
    // Update entity count
    _entityCount = World.CountEntities(new QueryDescription());  // ❌ Created in Update()
}
```

**Violation:** QueryDescription created in Update() method (hot path).

**Fix Required:**
- Cache as `private static readonly QueryDescription _allEntitiesQuery = new QueryDescription();`
- Use cached query in Update()

**Impact:** MEDIUM (allocation every frame)

---

### 4. TileSizeHelper - QueryDescription in Static Methods

**Location:** `MonoBall.Core/ECS/Utilities/TileSizeHelper.cs:46, 88`

**Issue:**
```csharp
public static int GetTileWidth(World world, IModManager? modManager = null)
{
    int tileWidth = 0;
    var mapQuery = new QueryDescription().WithAll<MapComponent>();  // ❌ Created in method
    world.Query(in mapQuery, ...);
}
```

**Violation:** QueryDescription created in static helper methods (called frequently).

**Fix Required:**
- Cache as `private static readonly QueryDescription MapQuery = new QueryDescription().WithAll<MapComponent>();`
- Use cached query in both `GetTileWidth()` and `GetTileHeight()`

**Impact:** MEDIUM (allocation per call, these methods are called frequently)

---

## 🟡 MEDIUM: Hacky Timing Workaround

### 1. GameInitializationService - Multiple Task.Yield() Calls

**Location:** `MonoBall.Core/GameInitializationService.cs:303-305`

**Issue:**
```csharp
// Give the main thread time to process the progress update
// We need multiple yields to ensure the update is processed and rendered
await Task.Yield();
await Task.Yield();
await Task.Yield();
```

**Problem:** Hacky workaround using multiple `Task.Yield()` calls to ensure progress updates are processed.

**Why This Is Bad:**
- Relies on timing/thread scheduling (non-deterministic)
- Comment admits uncertainty ("we need multiple yields")
- No guarantee this actually works reliably
- Better solutions exist (proper synchronization, events, etc.)

**Fix Required:**
- Use proper synchronization mechanism:
  - `SemaphoreSlim` to wait for progress update acknowledgment
  - Or use `LoadingSceneSystem.EnqueueProgress()` return value/event
  - Or use `TaskCompletionSource` to wait for specific state
- Remove multiple `Task.Yield()` calls

**Impact:** MEDIUM (works but fragile, non-deterministic)

---

## 🔴 CRITICAL: Event Subscription Memory Leak

### 1. SystemManager - Missing Event Unsubscription

**Location:** `MonoBall.Core/ECS/SystemManager.cs:378-383, 524-529, 1197-1250`

**Issue:**
```csharp
// Lines 378-383: Subscribe to events
EventBus.Subscribe<SceneCreatedEvent>(OnSceneCreated);
EventBus.Subscribe<SceneDestroyedEvent>(OnSceneDestroyed);
EventBus.Subscribe<SceneActivatedEvent>(OnSceneActivated);
EventBus.Subscribe<SceneDeactivatedEvent>(OnSceneDeactivated);
EventBus.Subscribe<ScenePausedEvent>(OnScenePaused);
EventBus.Subscribe<SceneResumedEvent>(OnSceneResumed);

// Lines 1197-1250: Dispose() method - NO UNSUBSCRIBE CALLS!
public void Dispose()
{
    // ... disposes systems ...
    // ❌ MISSING: EventBus.Unsubscribe calls!
}
```

**Violation:** SystemManager subscribes to 6 events but never unsubscribes in `Dispose()`, causing memory leaks.

**Problem:**
- EventBus holds references to SystemManager's event handlers
- SystemManager is never garbage collected while EventBus exists
- Memory leak: handlers accumulate if SystemManager is recreated

**Fix Required:**
- Add unsubscription calls in `Dispose()`:
```csharp
public void Dispose()
{
    if (_isDisposed) return;
    
    // Unsubscribe from events FIRST (before disposing systems)
    EventBus.Unsubscribe<SceneCreatedEvent>(OnSceneCreated);
    EventBus.Unsubscribe<SceneDestroyedEvent>(OnSceneDestroyed);
    EventBus.Unsubscribe<SceneActivatedEvent>(OnSceneActivated);
    EventBus.Unsubscribe<SceneDeactivatedEvent>(OnSceneDeactivated);
    EventBus.Unsubscribe<ScenePausedEvent>(OnScenePaused);
    EventBus.Unsubscribe<SceneResumedEvent>(OnSceneResumed);
    
    // ... rest of disposal ...
}
```

**Impact:** HIGH (memory leak, prevents GC of SystemManager)

---

## 🟡 MEDIUM: Dead Code / Unused Fallback

### 1. MovementSystem - Unused Fallback Query

**Location:** `MonoBall.Core/ECS/Systems/MovementSystem.cs:100-101`

**Issue:** Query `_movementQuery` is created but never used in the codebase.

**Fix Required:**
- Remove entirely (dead code)
- Search codebase to ensure no external code references it

**Impact:** LOW (dead code, but clutters codebase)

---

## 🟢 LOW: Code Quality Issues

### 1. MapPopupSystem - Incomplete Fallback Logic Comment

**Location:** `MonoBall.Core/ECS/Systems/MapPopupSystem.cs:266-272`

**Issue:** Comment says "Fallback" but doesn't explain why fallback is needed or what bug it's working around.

**Fix:** If keeping fallback (after fixing to fail fast), add clear comment explaining the scenario. Otherwise remove.

---

## Summary of Required Fixes

| Priority | Issue | Location | Fix |
|----------|-------|----------|-----|
| 🔴 CRITICAL | Backward compatibility query | MovementSystem.cs:100-101 | Remove dead code |
| 🔴 CRITICAL | Fallback scene cleanup | MapPopupSystem.cs:266-272 | Fail fast instead |
| 🔴 CRITICAL | Fallback background color | MonoBallGame.cs:371-372 | Fail fast instead |
| 🔴 CRITICAL | QueryDescription in ApplyTemplate | ShaderTemplateSystem.cs:66 | Cache as static readonly |
| 🔴 CRITICAL | QueryDescription in ClearLayer | ShaderTemplateSystem.cs:136 | Use cached query |
| 🔴 CRITICAL | QueryDescription in Update | PerformanceStatsSystem.cs:115 | Cache as static readonly |
| 🔴 CRITICAL | QueryDescription in helpers | TileSizeHelper.cs:46,88 | Cache as static readonly |
| 🔴 CRITICAL | Event subscription leak | SystemManager.cs:378-383 | Unsubscribe in Dispose() |
| 🟡 MEDIUM | Multiple Task.Yield workaround | GameInitializationService.cs:303-305 | Use proper synchronization |
| 🟡 MEDIUM | Unused fallback query | MovementSystem.cs:100-101 | Remove dead code |

---

## Recommended Fix Order

1. **First:** Fix SystemManager event subscription leak (memory leak - HIGH priority)
2. **Second:** Fix QueryDescription caching issues (performance impact)
3. **Third:** Remove backward compatibility code (rule violation)
4. **Fourth:** Fix fallback code to fail fast (rule violation)
5. **Fifth:** Fix Task.Yield workaround (fragile code)

---

## Testing Recommendations

After fixes:
1. Test movement system with ActiveMapEntity tag (ensure fallback removal doesn't break anything)
2. Test map popup cleanup (ensure fail-fast doesn't cause crashes in normal operation)
3. Test initialization timing (ensure proper synchronization works)
4. Performance test: Verify QueryDescription caching reduces allocations

---

## Notes

- All issues violate explicit project rules in `.cursorrules`
- Most fixes are straightforward (remove code, cache queries, fail fast)
- Task.Yield workaround requires more careful refactoring
- No bugs found that would cause crashes, but rule violations need addressing

