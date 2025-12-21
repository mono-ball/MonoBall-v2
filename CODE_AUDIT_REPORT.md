# MonoBall Code Audit Report
**Date:** 2025-01-27  
**Scope:** Complete architecture, ECS, SOLID/DRY, code smells, and potential bugs

## Executive Summary

This audit identified **27 issues** across multiple categories:
- **6 Critical Performance Issues** (QueryDescription in hot paths)
- **3 Event Bus Issues** (events created but not sent)
- **5 Memory Allocation Issues** (allocations in Update/Render)
- **3 Component Design Issues** (ECS best practices violations)
- **4 SOLID/DRY Violations**
- **3 Code Smells** (inconsistencies, unused code)
- **3 Potential Bugs** (missing validation, null checks)

---

## 1. CRITICAL PERFORMANCE ISSUES

### 1.1 QueryDescription Created in Hot Paths ⚠️ CRITICAL

**Issue:** `QueryDescription` instances are being created in `Update()` and `Render()` methods, causing unnecessary allocations every frame.

**Impact:** Significant performance degradation due to:
- Allocations on every frame (60+ times per second)
- GC pressure
- Violates Arch ECS best practices

**Locations:**

1. **CameraSystem.Update()** (Line 30)
```30:37:MonoBall/MonoBall.Core/ECS/Systems/CameraSystem.cs
var queryDescription = new QueryDescription().WithAll<CameraComponent>();
World.Query(
    in queryDescription,
    (ref CameraComponent camera) =>
    {
        UpdateCamera(ref camera, dt);
    }
);
```

2. **AnimatedTileSystem.Update()** (Line 36)
```36:46:MonoBall/MonoBall.Core/ECS/Systems/AnimatedTileSystem.cs
var queryDescription = new QueryDescription().WithAll<
    AnimatedTileDataComponent,
    TileDataComponent
>();
World.Query(
    in queryDescription,
    (ref AnimatedTileDataComponent animData, ref TileDataComponent tileData) =>
    {
        UpdateAnimations(ref animData, ref tileData, dt);
    }
);
```

3. **MapRendererSystem.Render()** (Line 86)
```86:91:MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs
var queryDescription = new QueryDescription().WithAll<
    TileChunkComponent,
    TileDataComponent,
    PositionComponent,
    RenderableComponent
>();
```

4. **MapRendererSystem.GetActiveCamera()** (Line 233)
```233:243:MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs
var cameraQuery = new QueryDescription().WithAll<CameraComponent>();
World.Query(
    in cameraQuery,
    (ref CameraComponent camera) =>
    {
        if (camera.IsActive)
        {
            activeCamera = camera;
        }
    }
);
```

5. **MapConnectionSystem.GetConnection()** (Line 78)
```78:94:MonoBall/MonoBall.Core/ECS/Systems/MapConnectionSystem.cs
var queryDescription = new QueryDescription().WithAll<
    MapComponent,
    MapConnectionComponent
>();

MapConnectionComponent? foundConnection = null;

World.Query(
    in queryDescription,
    (ref MapComponent mapComp, ref MapConnectionComponent connComp) =>
    {
        if (mapComp.MapId == mapId && connComp.Direction == direction)
        {
            foundConnection = connComp;
        }
    }
);
```

6. **SceneManagerSystem.CreateScene()** (Line 79)
```79:90:MonoBall/MonoBall.Core/ECS/Systems/SceneManagerSystem.cs
var cameraQuery = new QueryDescription().WithAll<CameraComponent>();
World.Query(
    in cameraQuery,
    (Entity entity, ref CameraComponent _) =>
    {
        if (entity.Id == cameraEntityId)
        {
            cameraFound = true;
            hasCameraComponent = true;
        }
    }
);
```

**Fix:** Cache `QueryDescription` instances as readonly instance fields, initialized in the constructor. See `NpcAnimationSystem` and `NpcRendererSystem` for correct patterns.

**Example Fix:**
```csharp
public partial class CameraSystem : BaseSystem<World, float>
{
    private readonly QueryDescription _queryDescription;
    
    public CameraSystem(World world) : base(world)
    {
        _queryDescription = new QueryDescription().WithAll<CameraComponent>();
    }
    
    public override void Update(in float deltaTime)
    {
        float dt = deltaTime;
        World.Query(in _queryDescription, (ref CameraComponent camera) =>
        {
            UpdateCamera(ref camera, dt);
        });
    }
}
```

---

## 2. EVENT BUS ISSUES

### 2.1 Events Created But Never Sent ⚠️ HIGH

**Issue:** Events are instantiated but never published to EventBus, causing silent failures and broken event-driven architecture.

**Locations:**

1. **MapLoaderSystem.LoadMap()** (Line 145-146)
```144:147:MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs
// Fire MapLoadedEvent
var loadedEvent = new MapLoadedEvent { MapId = mapId, MapEntity = mapEntity };
// Note: EventBus integration will be added when we integrate with game
```

2. **MapLoaderSystem.UnloadMap()** (Line 252-254)
```252:255:MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs
// Fire MapUnloadedEvent
var unloadedEvent = new MapUnloadedEvent { MapId = mapId };
// Note: EventBus integration will be added when we integrate with game
```

3. **MapConnectionSystem.TransitionToMap()** (Line 51-59)
```51:60:MonoBall/MonoBall.Core/ECS/Systems/MapConnectionSystem.cs
// Fire MapTransitionEvent
var transitionEvent = new MapTransitionEvent
{
    SourceMapId = sourceMapId,
    TargetMapId = targetMapId,
    Direction = direction,
    Offset = offset,
};
// Note: EventBus integration will be added when we integrate with game
```

**Fix:** Use `EventBus.Send(ref eventData)` to actually publish events, following the pattern used in `MapLoaderSystem.CreateNpcs()` (line 757) and `NpcAnimationSystem`.

**Example Fix:**
```csharp
var loadedEvent = new MapLoadedEvent { MapId = mapId, MapEntity = mapEntity };
EventBus.Send(ref loadedEvent);
Log.Information("Map loaded: {MapId}", mapId);
```

---

## 3. MEMORY ALLOCATION ISSUES

### 3.1 Allocations in Render/Update Hot Paths ⚠️ MEDIUM-HIGH

**Issue:** Collections are allocated in `Update()` and `Render()` methods, causing GC pressure.

**Locations:**

1. **MapRendererSystem.Render()** (Lines 93-100, 104-111)
```93:111:MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs
var chunks =
    new List<(
        Entity entity,
        TileChunkComponent chunk,
        TileDataComponent data,
        PositionComponent pos,
        RenderableComponent render
    )>();

// Query entities first, then get components for each
// This approach is needed because we require Entity references for optional component access
var entities = new List<Entity>();
World.Query(
    in queryDescription,
    (Entity entity) =>
    {
        entities.Add(entity);
    }
);
```

**Fix:** Reuse collections by storing them as instance fields and clearing them each frame.

2. **NpcRendererSystem.CollectVisibleNpcs()** (Line 123-130)
```123:130:MonoBall/MonoBall.Core/ECS/Systems/NpcRendererSystem.cs
var npcs =
    new List<(
        Entity entity,
        NpcComponent npc,
        SpriteAnimationComponent anim,
        PositionComponent pos,
        RenderableComponent render
    )>();
```

**Fix:** Same as above - reuse collection.

3. **SceneManagerSystem.CleanupDeadEntities()** (Line 433)
```433:440:MonoBall/MonoBall.Core/ECS/Systems/SceneManagerSystem.cs
// Remove dead entities from stack
var deadEntities = new List<Entity>();
foreach (var sceneEntity in _sceneStack)
{
    if (!World.IsAlive(sceneEntity))
    {
        deadEntities.Add(sceneEntity);
    }
}
```

**Fix:** Consider using a for-loop with backwards iteration to avoid allocation, or reuse a collection field.

4. **MapRendererSystem.Render()** - Sort allocation (Line 158)
```158:158:MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs
chunks = chunks.OrderBy(c => c.chunk.LayerIndex).ThenBy(c => c.chunk.LayerId).ToList();
```

**Fix:** Use `List<T>.Sort()` instead of LINQ `OrderBy().ToList()` to avoid allocation.

---

## 4. COMPONENT DESIGN ISSUES

### 4.1 Reference Type in Component (ECS Violation) ⚠️ HIGH

**Issue:** `AnimatedTileDataComponent` contains a `Dictionary<int, TileAnimationState>` which is a reference type, violating ECS best practices that components should be value types.

**Location:**
```15:15:MonoBall/MonoBall.Core/ECS/Components/AnimatedTileDataComponent.cs
public Dictionary<int, TileAnimationState> AnimatedTiles { get; set; }
```

**Impact:**
- Breaks ECS architecture principles
- Potential memory management issues
- Copy semantics are unclear (shallow vs deep copy)
- May cause issues with Arch ECS's component storage

**Fix Options:**
1. **Option A:** Use a fixed-size array or span if the number of animated tiles is bounded
2. **Option B:** Store animation data outside the component and reference it by ID
3. **Option C:** Accept the violation but document it clearly and ensure proper initialization/null checks

**Recommendation:** Document the architectural decision and ensure the Dictionary is always initialized. Consider Option B for better ECS compliance.

### 4.2 Component Initialization Missing

**Issue:** `AnimatedTileDataComponent.AnimatedTiles` can be null, but the code doesn't always check for null before use.

**Location:** While `AnimatedTileSystem` checks for null (line 61), other systems accessing this component may not.

**Fix:** Add defensive null checks or ensure initialization in `MapLoaderSystem.CreateTileChunks()`.

---

## 5. SOLID/DRY VIOLATIONS

### 5.1 Duplicate Camera Query Logic ⚠️ MEDIUM

**Issue:** Camera querying logic is duplicated across multiple systems.

**Locations:**
- `MapRendererSystem.GetActiveCamera()` (Line 229-246)
- `SceneManagerSystem.CreateScene()` (Line 79-90)
- `NpcRendererSystem` uses `ICameraService` (correct pattern)

**Fix:** All systems should use `ICameraService` for camera queries to follow DRY and DIP principles.

### 5.2 MapRendererSystem Double Query ⚠️ MEDIUM

**Issue:** `MapRendererSystem.Render()` queries entities first, then queries components again in a loop, violating single-pass query principle.

**Location:**
```104:143:MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs
var entities = new List<Entity>();
World.Query(
    in queryDescription,
    (Entity entity) =>
    {
        entities.Add(entity);
    }
);

// Get components for each entity and collect visible chunks
foreach (var entity in entities)
{
    ref var renderComp = ref World.Get<RenderableComponent>(entity);
    // ... get other components
}
```

**Fix:** Use a single-pass query with all required components, similar to `NpcRendererSystem.CollectVisibleNpcs()` pattern.

### 5.3 System Initialization Complexity ⚠️ MEDIUM

**Issue:** `SystemManager.Initialize()` is doing too much (violates SRP):
- Creating services
- Creating systems
- Grouping systems
- Setting up dependencies between systems

**Location:** `SystemManager.Initialize()` (Lines 164-244)

**Fix:** Extract system creation to a factory or builder pattern.

### 5.4 SceneManagerSystem Cleanup Called Manually ⚠️ LOW

**Issue:** `SceneManagerSystem.Cleanup()` must be called manually from `SystemManager.Dispose()`, but other systems don't have explicit cleanup methods.

**Location:**
```294:294:MonoBall/MonoBall.Core/ECS/SystemManager.cs
_sceneManagerSystem?.Cleanup();
```

**Fix:** Implement `IDisposable` properly on `SceneManagerSystem` and let disposal handle cleanup automatically.

---

## 6. CODE SMELLS & INCONSISTENCIES

### 6.1 Partial Class Without Source Generation ⚠️ LOW

**Issue:** Systems are marked as `partial class` but don't use Arch.System.SourceGenerator features.

**Locations:** All systems in `ECS/Systems/`

**Impact:** Unnecessary complexity, potential confusion

**Fix:** Remove `partial` keyword if source generation isn't used, or document why it's kept for future use.

### 6.2 Inconsistent Event Bus Usage ⚠️ MEDIUM

**Issue:** Some events are sent via `EventBus.Send()`, others are created but not sent (see Section 2).

**Fix:** Standardize event publishing across all systems.

### 6.3 NpcAnimationSystem Dispose Pattern ⚠️ LOW

**Issue:** `NpcAnimationSystem` uses `new void Dispose()` instead of properly implementing IDisposable pattern that works with base class.

**Location:**
```140:143:MonoBall/MonoBall.Core/ECS/Systems/NpcAnimationSystem.cs
public new void Dispose()
{
    Dispose(true);
    GC.SuppressFinalize(this);
}
```

**Fix:** If `BaseSystem` implements IDisposable, override `Dispose(bool disposing)` properly. Otherwise, the current pattern is acceptable but should be documented.

---

## 7. POTENTIAL BUGS

### 7.1 Missing Null Check in MapRendererSystem ⚠️ MEDIUM

**Issue:** `MapRendererSystem.RenderChunk()` accesses `AnimatedTileDataComponent` without checking if entity has the component first.

**Location:**
```366:366:MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs
ref var animData = ref World.Get<AnimatedTileDataComponent>(chunkEntity);
```

**Risk:** If a chunk entity doesn't have `AnimatedTileDataComponent` but `TileDataComponent.HasAnimatedTiles` is true (or incorrectly set), this will throw.

**Fix:** Add `World.Has<AnimatedTileDataComponent>(chunkEntity)` check before accessing.

### 7.2 Potential Race Condition in EventBus ⚠️ LOW

**Issue:** `EventBus` uses static `Dictionary` without thread synchronization.

**Location:** `EventBus.cs` (Line 12)

**Risk:** If events are published/subscribed from multiple threads (unlikely in MonoGame, but possible), dictionary modifications could cause issues.

**Fix:** Add thread synchronization if multi-threading is a concern, or document that EventBus is single-threaded only.

### 7.3 MapLoaderSystem Missing Validation ⚠️ LOW

**Issue:** `MapLoaderSystem.LoadMap()` doesn't validate that `tilePosition` is reasonable or that map dimensions are valid before creating entities.

**Location:** `MapLoaderSystem.LoadMap()` (Line 63)

**Fix:** Add validation for map dimensions and tile positions.

---

## 8. ARCHITECTURE ISSUES

### 8.1 EcsWorld Singleton Pattern ⚠️ LOW

**Issue:** `EcsWorld` uses a singleton pattern but is also wrapped in `EcsService`.

**Location:** `EcsWorld.cs` and `EcsService.cs`

**Impact:** Redundant abstraction, potential confusion

**Fix:** Consider removing singleton and using dependency injection throughout, or remove `EcsService` wrapper if singleton is preferred.

### 8.2 System Dependencies via Setter Methods ⚠️ MEDIUM

**Issue:** Some systems receive dependencies via setter methods rather than constructor injection, making dependencies less explicit.

**Locations:**
- `MapRendererSystem.SetSpriteBatch()` (Line 47)
- `NpcRendererSystem.SetSpriteBatch()` (Line 59)
- `SceneRendererSystem.SetSpriteBatch()`, `SetMapRendererSystem()`, `SetNpcRendererSystem()`

**Fix:** Consider constructor injection or a builder pattern for better dependency management.

---

## 9. ORGANIZATION ISSUES

### 9.1 File Organization - Components Look Good ✅

Components are well-organized in `ECS/Components/` directory with proper naming conventions.

### 9.2 File Organization - Systems Look Good ✅

Systems are well-organized in `ECS/Systems/` directory.

### 9.3 Mixed Responsibilities in MonoBallGame ⚠️ LOW

**Issue:** `MonoBallGame.LoadContent()` creates camera entities and initial scenes, mixing concerns.

**Location:** `MonoBallGame.LoadContent()` (Lines 147-203)

**Fix:** Extract scene/camera initialization to a separate initialization system or method.

---

## 10. SUMMARY OF PRIORITIES

### Critical (Fix Immediately)
1. ✅ Cache QueryDescription instances in all systems
2. ✅ Fix event bus issues (actually send events)

### High Priority (Fix Soon)
3. ✅ Fix AnimatedTileDataComponent reference type issue (document or refactor)
4. ✅ Reduce allocations in hot paths (reuse collections)
5. ✅ Fix double query in MapRendererSystem

### Medium Priority (Fix When Possible)
6. Extract camera query logic to ICameraService consistently
7. Add null checks for AnimatedTileDataComponent access
8. Refactor SystemManager initialization complexity
9. Standardize event publishing

### Low Priority (Nice to Have)
10. Remove unnecessary `partial` keywords
11. Fix NpcAnimationSystem dispose pattern
12. Add thread safety to EventBus if needed
13. Extract scene initialization from MonoBallGame

---

## 11. POSITIVE FINDINGS ✅

1. **Good Component Structure:** Most components follow ECS best practices (value types, pure data)
2. **Proper Event Subscription Cleanup:** `NpcAnimationSystem` and `SceneManagerSystem` properly unsubscribe from events
3. **Good Use of Interfaces:** `ICameraService`, `ITilesetLoaderService`, `ISpriteLoaderService` provide good abstractions
4. **Proper Error Handling:** Good use of logging and error handling throughout
5. **Documentation:** Good XML documentation on most public APIs
6. **Query Caching Examples:** `NpcAnimationSystem` and `NpcRendererSystem` demonstrate correct QueryDescription caching

---

## 12. RECOMMENDATIONS

1. **Establish Code Review Checklist:** Include QueryDescription caching, event publishing, and allocation checks
2. **Add Performance Profiling:** Use MonoGame profiling tools to measure impact of fixes
3. **Consider Unit Tests:** Add tests for event bus, system initialization, and component lifecycle
4. **Document Architectural Decisions:** Especially for violations like AnimatedTileDataComponent containing a Dictionary
5. **Create System Initialization Guide:** Document the proper way to initialize systems and their dependencies

