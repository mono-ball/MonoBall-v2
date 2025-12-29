# Window Animation Blocking - Architectural Analysis

## Current Implementation Issues

### 1. **Violation of Open/Closed Principle** ❌

**Problem**: `WindowAnimationSystem.DoesWindowBelongToBlockingScene()` hardcodes knowledge of specific window types:

- Message boxes: Window entity IS the scene entity
- Map popups: Window has `MapPopupComponent` with `SceneEntity` property

**Impact**: Adding a new window type (e.g., inventory windows, shop windows) requires modifying this method, violating
OCP.

```csharp
// Current implementation - fragile and not extensible
private bool DoesWindowBelongToBlockingScene(Entity windowEntity, List<Entity> blockingScenes)
{
    // Hardcoded check for message boxes
    if (windowEntity.Id == blockingScene.Id) { return true; }
    
    // Hardcoded check for map popups
    if (World.Has<MapPopupComponent>(windowEntity)) { ... }
    
    // What about future window types? Need to modify this method!
}
```

### 2. **Tight Coupling** ❌

**Problem**: `WindowAnimationSystem` is tightly coupled to:

- `MapPopupComponent` (specific component type)
- Knowledge of how message boxes work (window entity = scene entity)
- `SceneSystem` (via function dependency)

**Impact**: Changes to window types or scene architecture require changes to `WindowAnimationSystem`.

### 3. **Missing Abstraction** ❌

**Problem**: There's no explicit component or interface that represents "this window belongs to a scene". The
relationship is implicit:

- Message boxes: Window entity IS the scene (no explicit link)
- Map popups: `MapPopupComponent.SceneEntity` (explicit but type-specific)

**Impact**: No consistent way to query "all windows belonging to a scene" or "does this window belong to scene X".

### 4. **DRY Violation** ⚠️

**Problem**: Scene membership logic is duplicated:

- `MapPopupSceneSystem.RenderPopups()` checks `popup.SceneEntity.Id == sceneEntity.Id`
- `WindowAnimationSystem.DoesWindowBelongToBlockingScene()` checks the same relationship
- Future systems will need to duplicate this logic

**Impact**: Changes to scene membership rules require updates in multiple places.

### 5. **Single Responsibility Violation** ⚠️

**Problem**: `WindowAnimationSystem` is responsible for:

- Updating animation states (primary responsibility) ✅
- Determining scene membership (secondary concern) ❌
- Understanding different window types (knowledge it shouldn't have) ❌

**Impact**: System is harder to test, understand, and maintain.

### 6. **Performance Concerns** ⚠️

**Problem**:

- `GetBlockingScenes()` creates a new `List<Entity>` every frame
- `DoesWindowBelongToBlockingScene()` iterates through blocking scenes for each animation
- Multiple `World.Has<>` checks per animation

**Impact**: Unnecessary allocations and iterations in hot path.

### 7. **Integration Issues** ❌

**Problem**:

- Function dependency pattern (`Func<SceneSystem?>`) is inconsistent with other systems
- `WindowAnimationSystem` needs to know about `SceneSystem` but it's created before `SceneSystem`
- Creates temporal coupling (system must be initialized in specific order)

**Impact**: Fragile initialization order, harder to test in isolation.

## Comparison with Existing Patterns

### ✅ Good Pattern: `RenderingShaderComponent`

```csharp
public struct RenderingShaderComponent
{
    public Entity? SceneEntity { get; set; } // Explicit, generic scene ownership
}
```

**Why it's good**:

- Explicit scene ownership in component
- Generic (works for any scene type)
- Queryable (`shader.SceneEntity == sceneEntity`)

### ✅ Good Pattern: `ActiveMapFilterService`

```csharp
public bool IsEntityInActiveMaps(Entity entity)
{
    // Checks multiple component types but provides abstraction
    if (_world.TryGet<NpcComponent>(entity, out var npc)) { ... }
    if (_world.TryGet<MapComponent>(entity, out var map)) { ... }
}
```

**Why it's acceptable**:

- Provides abstraction layer (service pattern)
- Centralizes knowledge of entity-to-map relationships
- Can be extended without modifying callers

### ❌ Current Pattern: `WindowAnimationSystem`

**Why it's bad**:

- No abstraction layer
- Hardcoded in system logic
- Not extensible
- Violates SOLID principles

## Recommended Solutions

### Option 1: Add Generic Scene Ownership Component (Recommended) ⭐

**Create a generic component for scene ownership:**

```csharp
/// <summary>
/// Component that explicitly links an entity to a scene.
/// Used by windows, shaders, and other scene-scoped entities.
/// </summary>
public struct SceneOwnershipComponent
{
    /// <summary>
    /// Gets or sets the scene entity this entity belongs to.
    /// </summary>
    public Entity SceneEntity { get; set; }
}
```

**Benefits**:

- ✅ Explicit, queryable relationship
- ✅ Works for all window types
- ✅ Consistent with `RenderingShaderComponent` pattern
- ✅ Can query: `World.Query().WithAll<WindowAnimationComponent, SceneOwnershipComponent>()`
- ✅ No hardcoded type checks

**Implementation**:

1. Add `SceneOwnershipComponent` to window entities when created
2. Update `WindowAnimationSystem` to query for `SceneOwnershipComponent`
3. Check if `ownership.SceneEntity` is in blocking scenes list

### Option 2: Add Method to SceneSystem

**Add a method to check scene membership:**

```csharp
public bool DoesEntityBelongToBlockingScene(Entity entity)
{
    // Check if entity IS a blocking scene
    if (IsBlockingScene(entity)) return true;
    
    // Check if entity has SceneOwnershipComponent
    if (World.Has<SceneOwnershipComponent>(entity))
    {
        ref var ownership = ref World.Get<SceneOwnershipComponent>(entity);
        return IsBlockingScene(ownership.SceneEntity);
    }
    
    // Check legacy components (MapPopupComponent, etc.)
    if (World.Has<MapPopupComponent>(entity))
    {
        ref var popup = ref World.Get<MapPopupComponent>(entity);
        return IsBlockingScene(popup.SceneEntity);
    }
    
    return false;
}
```

**Benefits**:

- ✅ Centralizes scene membership logic
- ✅ Provides abstraction for `WindowAnimationSystem`
- ✅ Can handle legacy components during migration

**Drawbacks**:

- ⚠️ Still has hardcoded type checks (but centralized)
- ⚠️ Doesn't solve the root problem (missing explicit ownership)

### Option 3: Use ECS Query Filtering

**Filter animations by scene in the query:**

```csharp
// In WindowAnimationSystem.Update()
if (blockingScenes != null && blockingScenes.Count > 0)
{
    // Query for animations with SceneOwnershipComponent
    var sceneOwnershipQuery = new QueryDescription()
        .WithAll<WindowAnimationComponent, SceneOwnershipComponent>();
    
    World.Query(in sceneOwnershipQuery, (Entity entity, ref WindowAnimationComponent anim, ref SceneOwnershipComponent ownership) =>
    {
        if (blockingScenes.Contains(ownership.SceneEntity))
        {
            UpdateAnimation(entity, ref anim, dt, eventsToFire);
        }
    });
    
    // Also query for animations without scene ownership (legacy/global)
    // Only if no scenes are blocking
}
```

**Benefits**:

- ✅ Uses ECS query system effectively
- ✅ No manual iteration through blocking scenes
- ✅ More performant (ECS optimizes queries)

**Drawbacks**:

- ⚠️ Still need to handle legacy components
- ⚠️ More complex query logic

## Recommended Approach

**Combine Option 1 + Option 2**:

1. **Add `SceneOwnershipComponent`** for explicit scene ownership
2. **Add `SceneSystem.DoesEntityBelongToBlockingScene()`** to centralize logic
3. **Migrate existing windows** to use `SceneOwnershipComponent`:
    - Message boxes: Add component when creating scene
    - Map popups: Add component when creating popup (can coexist with `MapPopupComponent` during migration)
4. **Update `WindowAnimationSystem`** to use centralized method
5. **Remove hardcoded checks** once migration is complete

## Migration Path

1. **Phase 1**: Add `SceneOwnershipComponent` and `SceneSystem.DoesEntityBelongToBlockingScene()`
2. **Phase 2**: Update `WindowAnimationSystem` to use centralized method
3. **Phase 3**: Add `SceneOwnershipComponent` to new windows (message boxes, map popups)
4. **Phase 4**: Remove legacy component checks once all windows migrated
5. **Phase 5**: Remove `MapPopupComponent.SceneEntity` (or keep for backward compatibility)

## Conclusion

The current implementation is **hacky** because it:

- Hardcodes knowledge of specific window types
- Violates SOLID principles (OCP, SRP)
- Creates tight coupling
- Lacks proper abstraction
- Duplicates logic

The recommended solution provides:

- ✅ Explicit, queryable scene ownership
- ✅ Centralized scene membership logic
- ✅ Extensibility for future window types
- ✅ Better performance through ECS queries
- ✅ Cleaner architecture following ECS patterns

