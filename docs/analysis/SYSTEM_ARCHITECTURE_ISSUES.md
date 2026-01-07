# System Architecture Issues Analysis

## Executive Summary

The current architecture has **architectural inconsistencies** where scene-specific logic is split between ECS systems and scene systems, creating confusion and duplication. The pattern established by `GameSceneSystem`, `DebugBarSceneSystem`, and `LoadingSceneSystem` is not consistently followed for popups.

## Current Architecture Problems

### 1. Inconsistent Scene System Pattern

**Established Pattern (Good):**
- `GameSceneSystem` (in `Scenes.Systems`) - handles Update() and RenderScene() for game scenes
- `DebugBarSceneSystem` (in `Scenes.Systems`) - handles Update() and RenderScene() for debug bar scenes  
- `LoadingSceneSystem` (in `Scenes.Systems`) - handles Update() and RenderScene() for loading scenes

**Broken Pattern (Bad):**
- `MapPopupSystem` (in `ECS.Systems`) - handles Update(), RenderScene(), AND lifecycle management
- `MapPopupRendererSystem` (in `ECS.Systems`) - handles rendering
- `MapPopupSceneComponent` exists but no dedicated `MapPopupSceneSystem`

### 2. Responsibility Violations

**MapPopupSystem** is doing too much:
- ✅ Popup lifecycle (creation/destruction) - CORRECT
- ✅ Animation state updates - CORRECT  
- ❌ Rendering coordination (`RenderScene()`) - WRONG (should be in scene system)
- ❌ Camera resolution for rendering - WRONG (should be in scene system)

**MapPopupRendererSystem** is correctly scoped:
- ✅ Rendering popups - CORRECT

### 3. System Organization Issues

**Current Structure:**
```
ECS.Systems/
  ├── MapPopupSystem.cs          (lifecycle + update + render coordination)
  └── MapPopupRendererSystem.cs  (rendering)

Scenes.Systems/
  ├── GameSceneSystem.cs         (update + render coordination)
  ├── DebugBarSceneSystem.cs     (update + render coordination)
  ├── LoadingSceneSystem.cs      (update + render coordination)
  └── SceneRendererSystem.cs     (calls MapPopupSystem.RenderScene() ❌)
```

**Should Be:**
```
ECS.Systems/
  ├── MapPopupSystem.cs          (lifecycle + update only)
  └── MapPopupRendererSystem.cs  (rendering)

Scenes.Systems/
  ├── GameSceneSystem.cs         (update + render coordination)
  ├── DebugBarSceneSystem.cs     (update + render coordination)
  ├── LoadingSceneSystem.cs      (update + render coordination)
  ├── MapPopupSceneSystem.cs     (update + render coordination) ✨ NEW
  └── SceneRendererSystem.cs     (calls MapPopupSceneSystem.RenderScene() ✅)
```

## Detailed Analysis

### MapPopupSystem Current Responsibilities

```csharp
public class MapPopupSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    // ✅ CORRECT: Lifecycle management
    private void OnMapPopupShow(ref MapPopupShowEvent evt) { /* creates popup + scene */ }
    private void OnMapPopupHide(ref MapPopupHideEvent evt) { /* destroys popup + scene */ }
    
    // ✅ CORRECT: Animation updates
    public override void Update(in float deltaTime) { /* updates PopupAnimationComponent */ }
    
    // ❌ WRONG: Rendering coordination (should be in MapPopupSceneSystem)
    public void RenderScene(Entity sceneEntity, GameTime gameTime) { /* ... */ }
    private void RenderMapPopupScene(...) { /* calls MapPopupRendererSystem */ }
    private CameraComponent? GetActiveGameCamera() { /* ... */ }
}
```

### Comparison with Other Scene Systems

**GameSceneSystem Pattern:**
```csharp
public class GameSceneSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    // ✅ Update for scene entities
    public override void Update(in float deltaTime) { /* queries GameSceneComponent */ }
    
    // ✅ Render coordination (delegates to render systems)
    public void RenderScene(Entity sceneEntity, GameTime gameTime) 
    { 
        /* resolves camera, calls MapRendererSystem, SpriteRendererSystem, etc. */ 
    }
}
```

**MapPopupSystem Should Follow Same Pattern:**
```csharp
// ECS.Systems.MapPopupSystem - Lifecycle only
public class MapPopupSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    // ✅ Lifecycle management
    private void OnMapPopupShow(...) { /* creates popup + scene */ }
    private void OnMapPopupHide(...) { /* destroys popup + scene */ }
    
    // ✅ Animation updates (operates on popup entities, not scene entities)
    public override void Update(in float deltaTime) { /* updates PopupAnimationComponent */ }
    
    // ❌ REMOVE: RenderScene() - move to MapPopupSceneSystem
}

// Scenes.Systems.MapPopupSceneSystem - Scene coordination
public class MapPopupSceneSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    // ✅ Update for scene entities (if needed)
    public override void Update(in float deltaTime) { /* queries MapPopupSceneComponent */ }
    
    // ✅ Render coordination (delegates to MapPopupRendererSystem)
    public void RenderScene(Entity sceneEntity, GameTime gameTime) 
    { 
        /* resolves camera, calls MapPopupRendererSystem.Render() */ 
    }
}
```

## Proposed Refactoring

### Step 1: Create MapPopupSceneSystem

**Location:** `MonoBall.Core/Scenes/Systems/MapPopupSceneSystem.cs`

**Responsibilities:**
- Query for `MapPopupSceneComponent` entities in `Update()`
- Implement `RenderScene()` that resolves camera and calls `MapPopupRendererSystem`
- Follow same pattern as `GameSceneSystem`, `DebugBarSceneSystem`, `LoadingSceneSystem`

### Step 2: Refactor MapPopupSystem

**Changes:**
- Remove `RenderScene()` method
- Remove `RenderMapPopupScene()` method  
- Remove `GetActiveGameCamera()` method (or keep if needed for other purposes)
- Keep lifecycle management (`OnMapPopupShow`, `OnMapPopupHide`, `DestroyPopup`)
- Keep animation updates (`Update()`)

### Step 3: Update SceneRendererSystem

**Changes:**
- Replace `_mapPopupSystem.RenderScene()` with `_mapPopupSceneSystem.RenderScene()`
- Add `MapPopupSceneSystem` to constructor
- Update SystemManager to create and register `MapPopupSceneSystem`

### Step 4: Update SystemManager

**Changes:**
- Create `MapPopupSceneSystem` in `CreateSceneSpecificSystems()`
- Register it as an update system
- Pass it to `SceneRendererSystem` constructor
- Keep `MapPopupSystem` separate (for lifecycle + animation)

## Benefits of Refactoring

1. **Consistency** - All scene systems follow the same pattern
2. **Separation of Concerns** - Lifecycle/update logic separate from rendering coordination
3. **Maintainability** - Clear boundaries between ECS systems and scene systems
4. **Scalability** - Easy to add new scene types following established pattern
5. **Testability** - Scene systems can be tested independently

## Migration Impact

### Files to Modify:
1. `MonoBall.Core/ECS/Systems/MapPopupSystem.cs` - Remove rendering methods
2. `MonoBall.Core/Scenes/Systems/SceneRendererSystem.cs` - Use MapPopupSceneSystem
3. `MonoBall.Core/ECS/SystemManager.cs` - Create and register MapPopupSceneSystem

### Files to Create:
1. `MonoBall.Core/Scenes/Systems/MapPopupSceneSystem.cs` - New scene system

### Breaking Changes:
- `MapPopupSystem.RenderScene()` will no longer exist (internal method, no external callers)
- `SceneRendererSystem` constructor signature changes (adds MapPopupSceneSystem parameter)

## Additional Observations

### System Bloat in SystemManager

The `SystemManager` class is already **1263 lines** and growing. Issues:
- Too many system fields (30+)
- Complex initialization order dependencies
- Hard to understand system relationships

**Future Consideration:** Consider splitting SystemManager into:
- `CoreSystemManager` - Core ECS systems (map, sprite, camera, etc.)
- `SceneSystemManager` - Scene-specific systems
- `RenderSystemManager` - Rendering systems

But this is a larger refactoring beyond the current scope.

## Conclusion

The immediate fix is to:
1. Create `MapPopupSceneSystem` following the established pattern
2. Move rendering coordination from `MapPopupSystem` to `MapPopupSceneSystem`
3. Update `SceneRendererSystem` to use the new scene system

This will make the architecture consistent and easier to maintain as more scene types are added.

