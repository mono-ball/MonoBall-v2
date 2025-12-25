# Per-Scene Shader Support

**Date:** 2025-01-27  
**Status:** ✅ Implemented

---

## Overview

Per-scene shader support allows shaders to be scoped to specific scenes, enabling different visual effects for different scenes (e.g., different post-processing for different game areas).

---

## Architecture

### Component Changes

**`LayerShaderComponent`** now includes:
```csharp
/// <summary>
/// Optional scene entity this shader is associated with.
/// If null, the shader applies globally to all scenes.
/// If set, the shader only applies to the specified scene.
/// </summary>
public Entity? SceneEntity { get; set; }
```

### System Changes

**`ShaderManagerSystem`**:
- `UpdateShaderState(Entity? sceneEntity = null)` - Filters shaders by scene when provided
- `GetTileLayerShaderStack(Entity? sceneEntity = null)` - Returns shaders filtered by scene
- `GetSpriteLayerShaderStack(Entity? sceneEntity = null)` - Returns shaders filtered by scene
- `GetCombinedLayerShaderStack(Entity? sceneEntity = null)` - Returns shaders filtered by scene

**`SceneRendererSystem`**:
- Calls `UpdateShaderState(sceneEntity)` before rendering each scene
- Passes `sceneEntity` to `MapRendererSystem` and `SpriteRendererSystem`

**`MapRendererSystem`** and **`SpriteRendererSystem`**:
- `Render(GameTime gameTime, Entity? sceneEntity = null)` - Accepts optional scene entity
- Passes `sceneEntity` to shader queries

---

## Usage

### Global Shaders (Affect All Scenes)

Create a shader entity without setting `SceneEntity`:

```csharp
var globalShaderEntity = World.Create(new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:bloom",
    IsEnabled = true,
    RenderOrder = 0,
    SceneEntity = null // null = global, affects all scenes
});
```

### Per-Scene Shaders (Affect Specific Scene)

Create a shader entity with `SceneEntity` set:

```csharp
// Get the scene entity (e.g., from SceneManagerSystem)
Entity sceneEntity = GetSceneEntity("my-scene-id");

// Create shader scoped to this scene
var sceneShaderEntity = World.Create(new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:vignette",
    IsEnabled = true,
    RenderOrder = 0,
    SceneEntity = sceneEntity // Only affects this scene
});
```

### Attaching Shaders Directly to Scene Entities

You can also attach `LayerShaderComponent` directly to scene entities:

```csharp
// Get or create scene entity
Entity sceneEntity = GetOrCreateScene("my-scene-id");

// Add shader component directly to scene
World.Add(sceneEntity, new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:bloom",
    IsEnabled = true,
    RenderOrder = 0,
    SceneEntity = sceneEntity // Self-reference = per-scene
});
```

---

## Behavior

### Shader Filtering Rules

1. **Global Shaders** (`SceneEntity == null`):
   - Apply to all scenes
   - Always included when querying shaders

2. **Per-Scene Shaders** (`SceneEntity != null`):
   - Only apply to the specified scene
   - Excluded when querying shaders for other scenes

3. **Combined Behavior**:
   - When rendering a scene, both global shaders and per-scene shaders for that scene are included
   - Shaders are sorted by `RenderOrder` (lower = applied first)

### Example Scenarios

**Scenario 1: Global Bloom + Per-Scene Vignette**
```csharp
// Global bloom affects all scenes
var bloom = World.Create(new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:bloom",
    SceneEntity = null // Global
});

// Vignette only for specific scene
var vignette = World.Create(new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:vignette",
    SceneEntity = sceneEntity // Per-scene
});
```

**Result:** When rendering `sceneEntity`:
- Bloom shader applies (global)
- Vignette shader applies (per-scene)
- Both are applied in `RenderOrder` order

**Scenario 2: Different Shaders Per Scene**
```csharp
// Scene A: Bloom + Vignette
var sceneA = GetSceneEntity("scene-a");
World.Create(new LayerShaderComponent { ..., SceneEntity = sceneA, ShaderId = "bloom" });
World.Create(new LayerShaderComponent { ..., SceneEntity = sceneA, ShaderId = "vignette" });

// Scene B: Only Color Correction
var sceneB = GetSceneEntity("scene-b");
World.Create(new LayerShaderComponent { ..., SceneEntity = sceneB, ShaderId = "color-correction" });
```

**Result:**
- When rendering Scene A: Bloom + Vignette
- When rendering Scene B: Color Correction only

---

## Migration Guide

### Existing Code (No Changes Required)

Existing shader code continues to work:
- Shaders created without `SceneEntity` are global (backward compatible)
- `UpdateShaderState()` without parameters includes all shaders
- `Get*ShaderStack()` without parameters returns global shaders

### Upgrading to Per-Scene Shaders

1. **Identify shaders that should be per-scene**
2. **Get the scene entity** (from `SceneManagerSystem` or scene creation)
3. **Set `SceneEntity`** when creating shader components:
   ```csharp
   var shader = new LayerShaderComponent
   {
       // ... existing properties ...
       SceneEntity = sceneEntity // Add this
   };
   ```

---

## Technical Details

### Shader Query Flow

1. `SceneRendererSystem.Render()` iterates scenes
2. For each scene:
   - Calls `ShaderManagerSystem.UpdateShaderState(sceneEntity)`
   - Filters shaders: includes global (`SceneEntity == null`) and matching scene
   - Calls `MapRendererSystem.Render(gameTime, sceneEntity)`
   - Calls `SpriteRendererSystem.Render(gameTime, sceneEntity)`
   - Applies combined layer shaders via `GetCombinedLayerShaderStack(sceneEntity)`

### Performance Considerations

- Shader filtering happens at query time (no performance impact when not filtering)
- Global shaders are cached and reused across scenes
- Per-scene shaders are filtered efficiently using LINQ `.Where()`

---

## Examples

### Example 1: Scene-Specific Post-Processing

```csharp
// Create a dark, moody scene with vignette and desaturation
var darkScene = GetSceneEntity("dark-forest");

World.Create(new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:vignette",
    IsEnabled = true,
    RenderOrder = 0,
    Parameters = new Dictionary<string, object> { { "Intensity", 0.8f } },
    SceneEntity = darkScene
});

World.Create(new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:desaturate",
    IsEnabled = true,
    RenderOrder = 1,
    Parameters = new Dictionary<string, object> { { "Amount", 0.5f } },
    SceneEntity = darkScene
});
```

### Example 2: Global Effects + Scene Overrides

```csharp
// Global bloom for all scenes
World.Create(new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:bloom",
    SceneEntity = null // Global
});

// Extra bloom for bright scenes
var brightScene = GetSceneEntity("sunny-field");
World.Create(new LayerShaderComponent
{
    Layer = ShaderLayer.CombinedLayer,
    ShaderId = "base:shader:bloom",
    Parameters = new Dictionary<string, object> { { "Intensity", 2.0f } },
    SceneEntity = brightScene // Per-scene override
});
```

---

## Summary

✅ **Per-scene shader support is fully implemented**

- Shaders can be global (`SceneEntity == null`) or per-scene (`SceneEntity != null`)
- Global shaders apply to all scenes
- Per-scene shaders only apply to their specified scene
- Both can be combined (global + per-scene for same scene)
- Backward compatible (existing code works without changes)

---

**End of Documentation**

