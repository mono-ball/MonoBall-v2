# Uncommitted Changes Analysis

## Overview
Analysis of all uncommitted changes for architecture issues, Arch ECS/event issues, SOLID/DRY violations, and .cursorrules compliance.

**Date**: 2024-12-19
**Files Modified**: 6
**Files Added**: 5 (UI components, relationships, systems, design docs)

---

## Critical Issues

### 1. ❌ **UIRenderSystem.cs: QueryDescription Created in Hot Path**

**Location**: `MonoBall.Core/UI/Systems/UIRenderSystem.cs:150`

**Issue**: Creating `QueryDescription` in `RenderScene()` method (hot path) violates .cursorrules rule #3.

```csharp
// Line 150 - BAD: Creating QueryDescription in render method
World.Query(
    new QueryDescription().WithAll<CameraComponent>(),  // ❌ Created in hot path
    (Entity entity, ref CameraComponent cam) => { ... }
);
```

**Rule Violated**: 
> **ECS Systems**: Cache `QueryDescription` in constructor, never create queries in Update/Render

**Fix Required**: Cache `_cameraQuery` as an instance field in the constructor.

**Impact**: Performance - allocates QueryDescription every frame during rendering.

---

### 2. ❌ **MessageBoxSceneSystem.cs: Optional Dependency with Fail-Fast Exception**

**Location**: `MonoBall.Core/Scenes/Systems/MessageBoxSceneSystem.cs:241-245`

**Issue**: `UIRenderSystem` is marked as optional (`UIRenderSystem? uiRenderSystem = null`) but throws `InvalidOperationException` when null. This violates the "No Fallback Code" rule - either make it required or handle the null case properly.

```csharp
// Line 241-245 - BAD: Optional parameter but throws exception when null
if (_uiRenderSystem == null)
    throw new InvalidOperationException(
        "UIRenderSystem is required for message box rendering. "
            + "Ensure UIRenderSystem is created and passed to MessageBoxSceneSystem."
    );
```

**Rule Violated**: 
> **NO FALLBACK CODE** - Require all dependencies, throw `ArgumentNullException` for null

**Fix Required**: 
- Option A: Make `UIRenderSystem` required (remove `?` and `= null`, throw `ArgumentNullException` in constructor)
- Option B: If it's truly optional, handle null case gracefully (but this violates "No Fallback Code")

**Recommendation**: Make it required since the code cannot function without it.

**Impact**: Design inconsistency - parameter signature doesn't match behavior.

---

## Architecture Issues

### 3. ⚠️ **SpriteAnimationSystem.cs: World.Has<> in Hot Path**

**Location**: `MonoBall.Core/ECS/Systems/SpriteAnimationSystem.cs:108`

**Issue**: Using `World.Has<>` check in the Update loop for optional `SpriteSheetComponent`. While this is acceptable for optional components, it could be optimized.

```csharp
// Line 108 - Acceptable but could be optimized
if (World.Has<SpriteSheetComponent>(entity))
{
    ref var spriteSheet = ref World.Get<SpriteSheetComponent>(entity);
    // ...
}
```

**Analysis**: This is acceptable because:
- `SpriteSheetComponent` is optional (only players have it, not NPCs or UI sprites)
- The check is necessary to determine entity type
- Alternative would be separate queries, which adds complexity

**Recommendation**: Keep as-is. This is a reasonable trade-off for handling optional components.

**Impact**: Minor performance cost (one Has<> check per entity per frame).

---

### 4. ✅ **SpriteAnimationSystem.cs: Good Generic Query Refactoring**

**Location**: `MonoBall.Core/ECS/Systems/SpriteAnimationSystem.cs:49-57`

**Positive Change**: Refactored from separate `_npcQuery` and `_playerQuery` to a single generic `_animatedSpriteQuery`. This follows ECS principles - systems don't need to know about specific entity types.

```csharp
// GOOD: Generic query for all animated sprites
_animatedSpriteQuery = new QueryDescription().WithAll<
    SpriteComponent,
    SpriteAnimationComponent
>();
```

**Benefits**:
- More maintainable (works for NPCs, Players, UI sprites, etc.)
- Follows ECS principles (composition over inheritance)
- Reduces code duplication

**Impact**: Positive - better architecture.

---

## SOLID/DRY Issues

### 5. ⚠️ **MessageBoxSceneSystem.cs: Duplicate Camera Query Logic**

**Location**: Multiple locations in `MessageBoxSceneSystem.cs`

**Issue**: Camera query logic is duplicated in:
- `RenderScene()` method (line ~1980)
- `UpdateDownArrowPosition()` method (line ~750)
- `RenderMessageBoxText()` method (line ~1990)

**Example Duplication**:
```csharp
// Pattern repeated 3+ times:
CameraComponent? camera = null;
switch (scene.CameraMode)
{
    case SceneCameraMode.GameCamera:
        camera = _cameraService.GetActiveCamera();
        break;
    case SceneCameraMode.SceneCamera:
        if (scene.CameraEntityId.HasValue)
        {
            var cameraEntityId = scene.CameraEntityId.Value;
            World.Query(in _cameraQuery, (Entity entity, ref CameraComponent cam) =>
            {
                if (entity.Id == cameraEntityId)
                    camera = cam;
            });
        }
        break;
}
```

**Rule Violated**: DRY (Don't Repeat Yourself)

**Fix Required**: Extract to a private method:
```csharp
private CameraComponent? GetCameraForScene(Entity sceneEntity)
{
    if (!World.Has<SceneComponent>(sceneEntity))
        return null;
    
    ref var scene = ref World.Get<SceneComponent>(sceneEntity);
    // ... camera logic ...
}
```

**Impact**: Code maintainability - changes to camera logic must be made in 3+ places.

---

### 6. ⚠️ **UIRenderSystem.cs: Duplicate Camera Query Logic**

**Location**: `MonoBall.Core/UI/Systems/UIRenderSystem.cs:136-188`

**Issue**: Same camera query logic as MessageBoxSceneSystem (see issue #5). This logic should be extracted to a shared utility or service.

**Impact**: Code duplication across systems.

**Recommendation**: Consider creating a `CameraQueryHelper` utility class or extending `ICameraService` to handle scene camera queries.

---

### 7. ✅ **Component Design: Good Separation of Concerns**

**Positive**: UI components are well-designed:
- `UIElementComponent` - metadata (type, z-order, interactivity)
- `WindowComponent` - window-specific data (border, background, dimensions)
- `UITextComponent` - text rendering data
- `PositionComponent` - shared position (avoids duplication)

**Benefits**: 
- Single Responsibility Principle (SRP) - each component has one purpose
- No duplication - position is shared via `PositionComponent`
- Composable - entities can mix and match components

---

## Event System Issues

### 8. ✅ **Event Subscriptions: Properly Disposed**

**Location**: `MonoBall.Core/Scenes/Systems/MessageBoxSceneSystem.cs:190-192, 198-220`

**Positive**: Event subscriptions are properly stored in `_subscriptions` list and disposed in `Dispose()` method.

```csharp
_subscriptions.Add(EventBus.Subscribe<MessageBoxShowEvent>(OnMessageBoxShow));
_subscriptions.Add(EventBus.Subscribe<MessageBoxHideEvent>(OnMessageBoxHide));
// ... disposed in Dispose() ...
```

**Compliance**: ✅ Follows .cursorrules rule #5 (Event Subscriptions must implement IDisposable).

---

## .cursorrules Compliance

### 9. ✅ **QueryDescription Caching: Mostly Compliant**

**Status**: Mostly compliant, except for issue #1 (UIRenderSystem line 150).

**Compliant Examples**:
- `SpriteAnimationSystem`: Queries cached in constructor ✅
- `UIRenderSystem`: Main queries cached in constructor ✅
- `MessageBoxSceneSystem`: Queries cached in constructor ✅

**Non-Compliant**:
- `UIRenderSystem`: Line 150 creates QueryDescription in RenderScene ❌

---

### 10. ✅ **Component Naming: Compliant**

**Status**: All components end with `Component` suffix:
- `UIElementComponent` ✅
- `UITextComponent` ✅
- `WindowComponent` ✅
- `MessageBoxComponent` ✅

---

### 11. ✅ **Component Types: Value Types (struct)**

**Status**: All components are `struct` types:
- `UIElementComponent` - `struct` ✅
- `UITextComponent` - `struct` ✅
- `WindowComponent` - `struct` ✅
- `ContainsUIElement` - `struct` ✅
- `OwnsUIElement` - `struct` ✅

---

### 12. ✅ **System Inheritance: Compliant**

**Status**: All systems inherit from `BaseSystem<World, float>`:
- `UIRenderSystem` - `BaseSystem<World, float>` ✅
- `MessageBoxSceneSystem` - `BaseSystem<World, float>` ✅
- `SpriteAnimationSystem` - `BaseSystem<World, float>` ✅

---

### 13. ✅ **XML Documentation: Compliant**

**Status**: All public APIs have XML documentation:
- `UIRenderSystem` - documented ✅
- `MessageBoxSceneSystem` - documented ✅
- All components - documented ✅

---

### 14. ✅ **Dependency Injection: Compliant**

**Status**: All dependencies are injected via constructor with null checks:
- `UIRenderSystem` - throws `ArgumentNullException` for required params ✅
- `MessageBoxSceneSystem` - throws `ArgumentNullException` for required params ✅

**Exception**: Issue #2 - `UIRenderSystem` is optional but throws exception when null.

---

### 15. ✅ **Namespace Structure: Compliant**

**Status**: Namespaces match folder structure:
- `MonoBall.Core.UI.Components` - matches `UI/Components/` ✅
- `MonoBall.Core.UI.Relationships` - matches `UI/Relationships/` ✅
- `MonoBall.Core.UI.Systems` - matches `UI/Systems/` ✅

---

## Summary

### Critical Issues (Must Fix)
1. ❌ **UIRenderSystem.cs:150** - QueryDescription created in hot path
2. ❌ **MessageBoxSceneSystem.cs:241** - Optional dependency with fail-fast exception

### Architecture Issues (Should Fix)
3. ⚠️ **MessageBoxSceneSystem.cs** - Duplicate camera query logic (DRY violation)
4. ⚠️ **UIRenderSystem.cs** - Duplicate camera query logic (DRY violation)

### Minor Issues (Consider Fixing)
5. ⚠️ **SpriteAnimationSystem.cs:108** - World.Has<> in hot path (acceptable but could optimize)

### Positive Changes
- ✅ Generic query refactoring in SpriteAnimationSystem
- ✅ Good component design (SRP, no duplication)
- ✅ Proper event subscription disposal
- ✅ Overall .cursorrules compliance (except issues #1 and #2)

---

## Recommended Actions

### Priority 1 (Critical)
1. **Fix UIRenderSystem QueryDescription**: Cache `_cameraQuery` in constructor
2. **Fix MessageBoxSceneSystem UIRenderSystem dependency**: Make it required or handle null properly

### Priority 2 (Architecture)
3. **Extract camera query logic**: Create shared utility method for camera queries
4. **Consider CameraService extension**: Add scene camera query method to `ICameraService`

### Priority 3 (Optimization)
5. **Consider optimizing World.Has<>**: Only if profiling shows it's a bottleneck

---

## Code Quality Score

**Overall**: 8.5/10

**Breakdown**:
- Architecture: 9/10 (good ECS design, minor DRY violations)
- .cursorrules Compliance: 8/10 (2 critical violations)
- SOLID Principles: 9/10 (good component design, minor SRP issues)
- DRY: 7/10 (camera query logic duplicated)
- Performance: 8/10 (one QueryDescription allocation in hot path)
