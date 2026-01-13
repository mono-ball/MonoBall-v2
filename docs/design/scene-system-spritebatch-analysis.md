# Scene System SpriteBatch Usage Analysis

**Date:** 2025-01-XX  
**Status:** Analysis & Fixes Applied  
**Related Issue:** SpriteBatch.Begin() called when batch already active

---

## Summary

Analysis of all scene systems implementing `ISceneSystem` to identify discrepancies in `SpriteBatch` usage patterns. The coordinator (`SceneRenderingCoordinator`) manages `SpriteBatch` lifecycle, but some systems were incorrectly managing their own batches.

---

## Problem

The `SceneRenderingCoordinator` calls `SpriteBatch.Begin()` before calling each scene system's `RenderScene()` method. Scene systems should use the `SpriteBatch` from `IRenderContext` (which is already begun) rather than calling `Begin()`/`End()` on their own `SpriteBatch` instances.

**Error:** `System.InvalidOperationException: Begin cannot be called again until End has been successfully called.`

---

## Scene Systems Analysis

### ✅ **GameSceneSystem** - CORRECT

**Status:** ✅ No issues

**Pattern:**
- Uses `renderContext.SpriteBatch` correctly
- Delegates to `ElevationRendererSystem.Render()` which accepts `IRenderContext`
- No direct `Begin()`/`End()` calls

**Code:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
{
    // Render content (SpriteBatch already begun, viewport already set)
    _elevationRendererSystem.Render(gameTime, sceneEntity, renderContext);
    // No state management needed - coordinator handles it
}
```

---

### ✅ **MapPopupSceneSystem** - CORRECT

**Status:** ✅ No issues

**Pattern:**
- Correctly ends coordinator's batch and starts new one with `Matrix.Identity` for screen-space rendering
- Properly marks batch state using `MarkBatchEnded()`, `MarkNewBatchStarted()`, `MarkNewBatchEnded()`
- Uses `renderContext.SpriteBatch` throughout

**Code:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
{
    // End the coordinator's batch and begin a new one with Matrix.Identity
    renderContext.SpriteBatch.End();
    renderContext.MarkBatchEnded();
    
    renderContext.MarkNewBatchStarted();
    renderContext.SpriteBatch.Begin(..., Matrix.Identity);
    
    // Render using renderContext.SpriteBatch
    RenderPopupsWithViewport(..., renderContext.SpriteBatch, renderContext);
    
    renderContext.SpriteBatch.End();
    renderContext.MarkNewBatchEnded();
}
```

**Note:** `RenderMapPopupScene()` method exists but appears to be unused/deprecated. It incorrectly calls `Begin()`/`End()` on its own `_spriteBatch`, but this method is not called from `RenderScene()`.

---

### ✅ **MessageBoxSceneSystem** - CORRECT

**Status:** ✅ No issues

**Pattern:**
- Uses `RenderMessageBoxTextWithContext()` which correctly uses `renderContext.SpriteBatch`
- Delegates UI rendering to `UIRenderSystem` which handles batch management
- No direct `Begin()`/`End()` calls in active code path

**Code:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
{
    // Delegate UI rendering to UIRenderSystem
    _uiRenderSystem.RenderScene(sceneEntity, gameTime, renderContext);
    
    // Render text using renderContext.SpriteBatch (already begun)
    RenderMessageBoxTextWithContext(sceneEntity, ref msgBox, gameTime, renderContext);
}
```

**Note:** `RenderMessageBoxText()` method exists but appears to be unused/deprecated. It incorrectly calls `Begin()`/`End()` on its own `_spriteBatch`, but this method is not called from `RenderScene()`.

---

### ✅ **DebugBarSceneSystem** - FIXED

**Status:** ✅ Fixed in this analysis

**Previous Issue:**
- Called `Begin()`/`End()` on its own `_spriteBatch` field
- Ignored `renderContext.SpriteBatch` parameter

**Fix Applied:**
- Updated `RenderDebugBarScene()` to accept `IRenderContext`
- Removed `Begin()`/`End()` calls
- Updated `RenderDebugBar()` to accept `SpriteBatch` parameter
- All rendering now uses `renderContext.SpriteBatch`

**Code (Before):**
```csharp
private void RenderDebugBarScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime)
{
    _spriteBatch.Begin(..., Matrix.Identity);
    try {
        RenderDebugBar(gameTime);
    } finally {
        _spriteBatch.End();
    }
}
```

**Code (After):**
```csharp
private void RenderDebugBarScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime, IRenderContext renderContext)
{
    // Use renderContext.SpriteBatch which is already begun by the coordinator
    RenderDebugBar(gameTime, renderContext.SpriteBatch);
}
```

---

### ❌ **LoadingSceneSystem** - HAS ISSUE

**Status:** ❌ Needs Fix

**Issue:**
- `RenderScene()` calls `RenderLoadingScene()` which calls `Begin()`/`End()` on its own `_spriteBatch` field
- Does not use `renderContext.SpriteBatch` at all
- Will cause the same `InvalidOperationException` error

**Current Code:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
{
    // ...
    RenderLoadingScene(sceneEntity, ref scene, gameTime); // ❌ Doesn't pass renderContext
}

private void RenderLoadingScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime)
{
    _spriteBatch.Begin(..., Matrix.Identity); // ❌ Calls Begin() on own SpriteBatch
    try {
        RenderLoadingScreen(ref progress);
    } finally {
        _spriteBatch.End(); // ❌ Calls End() on own SpriteBatch
    }
}
```

**Required Fix:**
1. Update `RenderLoadingScene()` to accept `IRenderContext` parameter
2. Remove `Begin()`/`End()` calls
3. Update `RenderLoadingScreen()` to accept `SpriteBatch` parameter
4. Update helper methods (`DrawRectangle()`, `DrawRectangleOutline()`) to accept `SpriteBatch` parameter
5. Use `renderContext.SpriteBatch` throughout

---

### ✅ **DebugMenuSceneSystem** - CORRECT

**Status:** ✅ No issues

**Pattern:**
- Uses ImGui for rendering (doesn't use SpriteBatch)
- No `Begin()`/`End()` calls

**Code:**
```csharp
public void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext)
{
    // Render the ImGui overlay
    _debugOverlay.Draw();
}
```

---

## Summary Table

| System | Status | Uses renderContext | Has Begin/End | Notes |
|--------|--------|-------------------|---------------|-------|
| `GameSceneSystem` | ✅ Correct | Yes | No | Delegates to ElevationRendererSystem |
| `MapPopupSceneSystem` | ✅ Correct | Yes | Yes* | *Correctly manages batch state for Matrix.Identity |
| `MessageBoxSceneSystem` | ✅ Correct | Yes | No | Delegates to UIRenderSystem |
| `DebugBarSceneSystem` | ✅ Fixed | Yes | No | Fixed in this analysis |
| `LoadingSceneSystem` | ❌ **Issue** | No | Yes | **Needs fix** |
| `DebugMenuSceneSystem` | ✅ Correct | N/A | No | Uses ImGui |

---

## Recommended Fixes

### 1. Fix LoadingSceneSystem

**Changes Required:**
- Update `RenderLoadingScene()` signature to accept `IRenderContext`
- Remove `Begin()`/`End()` calls
- Update `RenderLoadingScreen()` to accept `SpriteBatch` parameter
- Update `DrawRectangle()` and `DrawRectangleOutline()` to accept `SpriteBatch` parameter
- Pass `renderContext.SpriteBatch` to all rendering methods

**Pattern to Follow:**
Same as `DebugBarSceneSystem` fix - use `renderContext.SpriteBatch` which is already begun by the coordinator.

---

## Best Practices

1. **Always use `renderContext.SpriteBatch`** - The coordinator manages batch lifecycle
2. **Never call `Begin()`/`End()` on your own `SpriteBatch`** - Unless you need different settings and properly manage state
3. **If you need different settings** (e.g., `Matrix.Identity` for screen-space):
   - End coordinator's batch: `renderContext.SpriteBatch.End()`
   - Mark batch ended: `renderContext.MarkBatchEnded()`
   - Begin new batch: `renderContext.SpriteBatch.Begin(...)`
   - Mark new batch started: `renderContext.MarkNewBatchStarted()`
   - End new batch: `renderContext.SpriteBatch.End()`
   - Mark new batch ended: `renderContext.MarkNewBatchEnded()`
4. **Pass `SpriteBatch` as parameter** - Don't use instance fields for rendering methods

---

## Related Files

- `MonoBall.Core/Scenes/Systems/SceneRenderingCoordinator.cs` - Manages batch lifecycle
- `MonoBall.Core/Scenes/Systems/IRenderContext.cs` - Interface for render context
- `MonoBall.Core/Scenes/Systems/RenderContext.cs` - Render context implementation
- `MonoBall.Core/Scenes/Systems/DebugBarSceneSystem.cs` - Fixed example
- `MonoBall.Core/Scenes/Systems/LoadingSceneSystem.cs` - Needs fix
