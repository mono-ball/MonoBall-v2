# Shader Support Design - Quick Reference

**Full Design:** See [SHADER_SUPPORT_DESIGN.md](./SHADER_SUPPORT_DESIGN.md)

---

## Overview

Add shader support to MonoBall's rendering pipeline, enabling visual effects on:
- **Tile Layers** - All tiles rendered by `MapRendererSystem`
- **Sprite Layers** - All sprites rendered by `SpriteRendererSystem`
- **Combined Layers** - Post-processing effects on entire screen

---

## Architecture Summary

### Components
- `LayerShaderComponent` - ECS component for layer-wide shader configuration
- `ShaderComponent` - Future: Per-entity shader configuration

### Systems
- `ShaderManagerSystem` - Manages active shaders and updates parameters
- `ShaderService` - Loads and caches shader effects from content

### Integration Points
- `MapRendererSystem` - Applies tile layer shaders
- `SpriteRendererSystem` - Applies sprite layer shaders
- `SceneRendererSystem` - Applies combined layer shaders (via render targets)

---

## Shader Types

### Tile Layer Shaders
**Examples:**
- Color grading (sepia, grayscale, filters)
- Weather effects (rain, fog)
- Time-of-day effects (day/night cycle)
- Lighting (ambient occlusion, shadows)

**Location:** `Content/Shaders/TileLayer/*.fx`

### Sprite Layer Shaders
**Examples:**
- Outline effects
- Glow/shadow effects
- Status effect tints (poison, frozen)
- Animation effects

**Location:** `Content/Shaders/SpriteLayer/*.fx`

### Combined Layer Shaders
**Examples:**
- Bloom
- Vignette
- Screen shake/flash
- CRT scanlines
- Color correction

**Location:** `Content/Shaders/CombinedLayer/*.fx`

---

## Implementation Phases

1. **Foundation** - Core infrastructure (ShaderService, components, systems)
2. **Tile Layer** - Integrate shaders into MapRendererSystem
3. **Sprite Layer** - Integrate shaders into SpriteRendererSystem
4. **Combined Layer** - Post-processing via render targets
5. **Content Pipeline** - Add example shaders and documentation

---

## Key Design Decisions

### ✅ Fail Fast
- Required dependencies throw `ArgumentNullException`
- Missing shaders log warnings but don't crash (graceful degradation)

### ✅ ECS Architecture
- Shader configuration via `LayerShaderComponent`
- `ShaderManagerSystem` queries components and manages state
- Rendering systems get shaders from `ShaderManagerSystem`

### ✅ Performance
- LRU cache for loaded shaders (max 20)
- Cache active shaders per layer (avoid queries every frame)
- Render targets only created when combined shaders active

### ✅ Content Pipeline
- Shaders compiled via MonoGame Content Pipeline (.mgcb)
- Naming convention: `{LayerType}{EffectName}.fx`
- Example: `TileLayerColorGrading.fx` → Shader ID: `"TileLayerColorGrading"`

---

## Example Usage

### Enable Tile Layer Shader
```csharp
var shaderComponent = new LayerShaderComponent
{
    ShaderId = "TileLayerColorGrading",
    Layer = ShaderLayer.TileLayer,
    IsEnabled = true,
    Parameters = new Dictionary<string, object>
    {
        ["Brightness"] = 0.1f,
        ["Contrast"] = 1.2f,
        ["Saturation"] = 0.8f
    }
};

// Attach to scene entity or global entity
World.Add(sceneEntity, shaderComponent);
```

### Enable Sprite Layer Shader
```csharp
var shaderComponent = new LayerShaderComponent
{
    ShaderId = "SpriteLayerOutline",
    Layer = ShaderLayer.SpriteLayer,
    IsEnabled = true,
    Parameters = new Dictionary<string, object>
    {
        ["OutlineColor"] = Color.Black,
        ["OutlineWidth"] = 2.0f
    }
};
```

### Enable Combined Layer Shader
```csharp
var shaderComponent = new LayerShaderComponent
{
    ShaderId = "CombinedLayerVignette",
    Layer = ShaderLayer.CombinedLayer,
    IsEnabled = true,
    Parameters = new Dictionary<string, object>
    {
        ["VignetteIntensity"] = 0.5f,
        ["VignetteRadius"] = 0.7f
    }
};
```

---

## File Structure

```
MonoBall.Core/
├── Content/
│   ├── Shaders/
│   │   ├── TileLayer/
│   │   │   ├── ColorGrading.fx
│   │   │   ├── Sepia.fx
│   │   │   └── Fog.fx
│   │   ├── SpriteLayer/
│   │   │   ├── Outline.fx
│   │   │   ├── Glow.fx
│   │   │   └── Shadow.fx
│   │   └── CombinedLayer/
│   │       ├── Bloom.fx
│   │       ├── Vignette.fx
│   │       └── Scanlines.fx
│   └── MonoBall.mgcb
├── ECS/
│   ├── Components/
│   │   └── LayerShaderComponent.cs
│   └── Systems/
│       └── ShaderManagerSystem.cs
└── Rendering/
    ├── IShaderService.cs
    └── ShaderService.cs
```

---

## Next Steps

1. Review design plan
2. Begin Phase 1: Foundation implementation
3. Create initial shader examples
4. Integrate with rendering systems

