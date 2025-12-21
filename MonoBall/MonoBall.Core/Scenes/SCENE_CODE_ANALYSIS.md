# Scene System Code Analysis

## Summary

This document analyzes the scene system code for architecture issues, Arch ECS issues, SOLID/DRY violations, inconsistencies, and potential bugs.

**Date:** 2025-01-27

---

## Critical Issues

### 1. EventBus Not Integrated (Architecture)

**Severity:** High  
**Location:** `SceneManagerSystem.cs`, all event files

**Issue:**
- All event publishing is commented out with TODOs
- Events are defined but never actually published
- `OnSceneMessage` handler is never subscribed to EventBus
- No event-driven communication is working

**Impact:**
- Scene system cannot communicate with other systems
- Inter-scene messaging doesn't work
- Other systems cannot react to scene lifecycle events

**Fix:**
- Integrate Arch.EventBus
- Publish all events after state changes
- Subscribe to `SceneMessageEvent` in constructor
- Unsubscribe in `Cleanup()` method

---

### 2. Dead Entity Cleanup (Potential Bug)

**Severity:** High  
**Location:** `SceneManagerSystem.cs:17, 343-348`

**Issue:**
- `_sceneStack` can contain dead entities
- Dead entities are only checked during iteration, not removed from stack
- `_sceneIds` dictionary can become stale with dead entity references
- `SortSceneStack()` can fail or produce incorrect results with dead entities

**Impact:**
- Memory leaks (dead entities remain in collections)
- Incorrect scene ordering
- Potential crashes when accessing dead entities

**Fix:**
- Add periodic cleanup method to remove dead entities
- Clean up in `Update()` or before `SortSceneStack()`
- Remove from both `_sceneStack` and `_sceneIds`

---

### 3. Viewport Not Restored (Bug)

**Severity:** Medium  
**Location:** `SceneRendererSystem.cs:330, 335`

**Issue:**
```csharp
// Save original viewport
var savedViewport = _graphicsDevice.Viewport;

_mapRendererSystem.Render(gameTime);

// Viewport is never restored!
```

**Impact:**
- Viewport state leaks between scene renders
- Subsequent scenes may render with incorrect viewport
- Screen-space scenes may be affected

**Fix:**
- Restore viewport after `RenderGameScene()` completes
- Use try-finally to ensure restoration even on exceptions

---

### 4. Null Camera Handling (Potential Bug)

**Severity:** Medium  
**Location:** `SceneRendererSystem.cs:195-205`

**Issue:**
- `GetActiveGameCamera()` can return `null`
- `RenderScene()` checks `camera.HasValue` but warning is logged and method returns
- Scene with `GameCamera` mode won't render if no active camera exists
- No fallback or error recovery

**Impact:**
- Scenes silently fail to render
- User sees blank screen with no indication of problem

**Fix:**
- Consider fallback behavior (e.g., render with identity transform)
- Or ensure camera always exists before creating GameScene
- Document requirement in XML comments

---

## Architecture Issues

### 5. SceneManagerSystem Has Too Many Responsibilities (SOLID Violation)

**Severity:** Medium  
**Location:** `SceneManagerSystem.cs`

**Issue:**
- Manages scene lifecycle (create/destroy)
- Manages scene state (active/paused/priority)
- Handles scene updates
- Handles scene events
- Maintains scene stack ordering

**Impact:**
- Violates Single Responsibility Principle
- Hard to test individual responsibilities
- Hard to extend with new scene behaviors

**Recommendation:**
- Consider splitting into:
  - `SceneLifecycleSystem` (create/destroy)
  - `SceneStateSystem` (active/paused/priority)
  - `SceneUpdateSystem` (update logic)
- Or keep as-is but document that it's a "manager" system (acceptable pattern)

---

### 6. Tight Coupling Between Systems (SOLID Violation)

**Severity:** Medium  
**Location:** `SceneRendererSystem.cs:19, 55-59`

**Issue:**
- `SceneRendererSystem` directly depends on `SceneManagerSystem` and `MapRendererSystem`
- Constructor injection creates tight coupling
- Hard to test in isolation

**Impact:**
- Violates Dependency Inversion Principle
- Difficult to mock for testing
- Changes to dependencies affect this system

**Recommendation:**
- Consider using interfaces (`ISceneManager`, `IMapRenderer`)
- Or accept coupling as acceptable for game systems (common pattern)

---

### 7. Duplicate Scene Iteration Logic (DRY Violation)

**Severity:** Low  
**Location:** `SceneManagerSystem.cs:340-367`, `SceneRendererSystem.cs:79-129`, `SceneInputSystem.cs:44-75`

**Issue:**
- Same pattern repeated in three systems:
  1. Get scene stack
  2. Iterate scenes
  3. Check if alive
  4. Check if active
  5. Process scene
  6. Check blocking flag and break

**Impact:**
- Code duplication
- Changes to iteration logic must be made in multiple places
- Inconsistent behavior if one system is updated but others aren't

**Recommendation:**
- Extract to helper method in `SceneManagerSystem`
- Or create `SceneIterator` utility class
- Or use extension methods on `IReadOnlyList<Entity>`

---

## Arch ECS Issues

### 8. Manual Queries Instead of Source-Generated (Performance)

**Severity:** Low  
**Location:** `SceneRendererSystem.cs:161-172, 216-232`

**Issue:**
- Uses manual `QueryDescription` and `World.Query()` instead of source-generated queries
- Less efficient than source-generated queries
- No compile-time safety

**Impact:**
- Slightly worse performance
- No compile-time checks for component existence

**Recommendation:**
- Convert to source-generated queries using `[Query]` attribute
- Follow pattern from other systems in codebase

---

### 9. Entity Reference Storage (Arch ECS Best Practice)

**Severity:** Low  
**Location:** `SceneManagerSystem.cs:17-18`

**Issue:**
- Stores `Entity` references in collections (`_sceneStack`, `_sceneIds`)
- Entities can become invalid if destroyed elsewhere

**Impact:**
- Need to check `World.IsAlive()` before use (already done, but adds overhead)
- Potential for stale references

**Note:**
- This is acceptable if properly handled (which it is)
- Alternative would be to store scene IDs only and look up entities each time (less efficient)

---

### 10. Component Boxing Workaround (Potential Bug)

**Severity:** Medium  
**Location:** `SceneManagerSystem.cs:60-73`

**Issue:**
```csharp
// Verify and fix the SceneComponent if it wasn't stored correctly (boxing issue with structs)
ref var storedSceneComponent = ref World.Get<SceneComponent>(sceneEntity);
if (string.IsNullOrEmpty(storedSceneComponent.SceneId) || ...)
{
    storedSceneComponent = sceneComponent;
    Log.Information("...");
}
```

**Impact:**
- Suggests a deeper problem with struct component storage
- Workaround may hide the real issue
- Could indicate incorrect usage of Arch ECS

**Recommendation:**
- Investigate why component isn't stored correctly
- Check if this is a known Arch ECS issue or misuse
- Consider if `SceneComponent` should be a class instead of struct (unlikely, structs are preferred)

---

## Inconsistencies

### 11. Inconsistent Query Patterns

**Severity:** Low  
**Location:** Multiple systems

**Issue:**
- Some systems use source-generated queries (`[Query]` attribute)
- Scene systems use manual queries
- No consistent pattern across codebase

**Impact:**
- Code inconsistency
- Harder to maintain

**Recommendation:**
- Standardize on source-generated queries where possible
- Document when manual queries are necessary

---

### 12. Inconsistent Event Publishing

**Severity:** Medium  
**Location:** `SceneManagerSystem.cs` (all event publishing)

**Issue:**
- Events are created but never published
- Logging is done instead of event publishing
- No actual event-driven communication

**Impact:**
- Events are defined but unused
- Other systems cannot subscribe to scene events
- Violates event-driven architecture

**Fix:**
- Integrate EventBus and publish all events
- Keep logging for debugging, but add event publishing

---

### 13. Inconsistent Parameter Types

**Severity:** Low  
**Location:** `SceneRendererSystem.cs:65, 344`

**Issue:**
- `Render()` method takes `GameTime`
- `Update()` method takes `float deltaTime`
- Inconsistent with other systems that use `float deltaTime` everywhere

**Impact:**
- Inconsistency in API
- Confusion about which to use

**Recommendation:**
- Standardize on `float deltaTime` for all systems
- Or document why `GameTime` is needed for rendering

---

## Potential Bugs

### 14. Scene ID Uniqueness Not Enforced at Component Level

**Severity:** Low  
**Location:** `SceneComponent.cs`, `SceneManagerSystem.cs:50-55`

**Issue:**
- Uniqueness is checked in `CreateScene()` but not enforced at component level
- Could create scenes with duplicate IDs if `CreateScene()` is bypassed
- No validation in `SceneComponent` itself

**Impact:**
- Potential for duplicate scene IDs if system is misused
- Lookup by ID could return wrong scene

**Recommendation:**
- Add validation in `SetScenePriority()` and other methods that accept sceneId
- Or document that `CreateScene()` must always be used

---

### 15. CameraEntityId Validation Missing

**Severity:** Low  
**Location:** `SceneComponent.cs:33`, `SceneRendererSystem.cs:154-192`

**Issue:**
- `CameraEntityId` can be set to any integer
- No validation that entity exists or has `CameraComponent`
- Validation only happens at render time

**Impact:**
- Invalid camera IDs cause runtime errors
- No early detection of misconfiguration

**Recommendation:**
- Add validation in `CreateScene()` when `CameraMode == SceneCamera`
- Or document requirement and validate at render time (current approach)

---

### 16. Scene Stack Sorting Stability

**Severity:** Low  
**Location:** `SceneManagerSystem.cs:372-397`

**Issue:**
- `SortSceneStack()` uses entity ID as tiebreaker for equal priorities
- Entity IDs are not guaranteed to be stable or meaningful for ordering
- Comment says "newer scenes on top" but entity ID doesn't guarantee this

**Impact:**
- Unpredictable ordering for scenes with same priority
- May not match intended behavior

**Recommendation:**
- Use creation timestamp or insertion order index instead
- Or document that entity ID ordering is arbitrary

---

## Code Quality Issues

### 17. Excessive Logging in Render Loop

**Severity:** Low  
**Location:** `SceneRendererSystem.cs:74-128`

**Issue:**
- Multiple `Log.Debug()` calls in hot path (render loop)
- Logging can be expensive
- Should use conditional compilation or log levels

**Impact:**
- Performance impact in release builds
- Log spam in debug builds

**Recommendation:**
- Remove or conditionally compile debug logs
- Use `Log.Verbose()` instead of `Log.Debug()` for detailed tracing
- Or use `#if DEBUG` preprocessor directives

---

### 18. Unused GameSceneComponent Parameter

**Severity:** Low  
**Location:** `SceneRendererSystem.cs:270-271, 312-318`

**Issue:**
- `RenderGameScene()` receives `gameSceneComponent` parameter but never uses it
- Marker component has no data, so this is expected

**Impact:**
- Minor code smell
- Confusing parameter name

**Recommendation:**
- Remove unused parameter or document why it's needed for future use
- Or use `_` to indicate intentionally unused

---

### 19. Missing Null Checks

**Severity:** Low  
**Location:** `SceneRendererSystem.cs:67-71, 320-327`

**Issue:**
- Some null checks are present (`_spriteBatch`, `_mapRendererSystem`)
- But missing checks in other places (e.g., `_sceneManagerSystem` in constructor)

**Impact:**
- Potential `NullReferenceException` if misconfigured

**Note:**
- Constructor already validates with `ArgumentNullException`, so this is acceptable

---

## Recommendations Summary

### High Priority
1. ✅ Integrate EventBus and publish all events
2. ✅ Add dead entity cleanup to `SceneManagerSystem`
3. ✅ Fix viewport restoration in `SceneRendererSystem`

### Medium Priority
4. ✅ Improve null camera handling with fallback or validation
5. ✅ Consider splitting `SceneManagerSystem` responsibilities (or document as acceptable)
6. ✅ Extract duplicate scene iteration logic to helper method
7. ✅ Fix component boxing workaround or investigate root cause

### Low Priority
8. Convert manual queries to source-generated queries
9. Standardize on consistent query patterns
10. Reduce logging in render loop
11. Add validation for `CameraEntityId` at creation time
12. Improve scene stack sorting stability

---

## Positive Aspects

✅ **Good separation of concerns** between systems (Manager, Renderer, Input)  
✅ **Proper use of ECS components** (SceneComponent is pure data)  
✅ **Good documentation** with XML comments  
✅ **Proper null checking** in constructors  
✅ **Good error handling** with logging  
✅ **Follows Arch ECS patterns** (BaseSystem, World queries)  
✅ **Event-driven architecture** (even if not yet integrated)  
✅ **Proper disposal** in SystemManager

---

## Conclusion

The scene system is well-architected overall but has several issues that should be addressed:

1. **Critical:** EventBus integration is missing, preventing event-driven communication
2. **Critical:** Dead entity cleanup is missing, causing potential memory leaks
3. **Important:** Viewport restoration bug will cause rendering issues
4. **Important:** Null camera handling needs improvement

Most other issues are code quality improvements that can be addressed incrementally.

