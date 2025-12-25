# Advanced Shader Features Analysis

**Generated:** 2025-01-27  
**Scope:** Analysis of missing advanced shader features from design document

---

## Executive Summary

The current shader infrastructure supports **basic shader functionality** but is missing several advanced features outlined in the design document:

**Current Status:**
- ✅ Basic shader support (single shader per layer)
- ✅ Shader parameter animation (single animation per parameter)
- ✅ Post-processing (single pass, one shader)
- ❌ Shader stacking/composition (only one shader selected)
- ❌ Multiple render passes
- ❌ Depth buffer support
- ❌ Shader blend modes
- ❌ Animation timelines/keyframes
- ❌ Shader inheritance/composition

---

## 1. Shader Stacking / Composition (Lines 1812-1815)

### Current Implementation

**Status:** ❌ **NOT IMPLEMENTED** - Only one shader per layer is selected

**Current Behavior:**
- `ShaderManagerSystem.UpdateLayerShader()` selects **only the shader with lowest RenderOrder**
- Multiple shaders on the same layer are ignored (only one active)
- No blend mode support

**Location:** `ShaderManagerSystem.cs:252-330`

```csharp
// Current: Only selects ONE shader
private void UpdateLayerShader(...)
{
    // Sort by RenderOrder (lowest first)
    shaders.Sort((a, b) => a.shader.RenderOrder.CompareTo(b.shader.RenderOrder));
    var selected = shaders[0];  // Only uses first shader!
    // ...
}
```

### What's Missing

1. **Multiple Shader Selection**: Need to select ALL enabled shaders, not just one
2. **Blend Mode Enum**: Need `ShaderBlendMode` enum (Replace, Multiply, Add, Overlay, Screen)
3. **Shader Stack Management**: Need to manage a list of shaders per layer, not single shader
4. **Blend Mode Application**: Need to apply blend modes when composing shaders

### Required Changes

#### 1.1 Add Blend Mode Enum

```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Blend mode for shader composition when multiple shaders are active.
    /// </summary>
    public enum ShaderBlendMode
    {
        /// <summary>
        /// Replace previous shader output (default).
        /// </summary>
        Replace,

        /// <summary>
        /// Multiply with previous shader output.
        /// </summary>
        Multiply,

        /// <summary>
        /// Add to previous shader output.
        /// </summary>
        Add,

        /// <summary>
        /// Overlay blend mode.
        /// </summary>
        Overlay,

        /// <summary>
        /// Screen blend mode.
        /// </summary>
        Screen,
    }
}
```

#### 1.2 Update LayerShaderComponent

```csharp
public struct LayerShaderComponent
{
    // ... existing fields ...

    /// <summary>
    /// Blend mode for shader composition when multiple shaders are active.
    /// Only used when multiple shaders have the same RenderOrder or are stacked.
    /// </summary>
    public ShaderBlendMode BlendMode { get; set; }
}
```

#### 1.3 Update ShaderManagerSystem

**Change from:**
```csharp
private Effect? _activeTileLayerShader;  // Single shader
```

**To:**
```csharp
private List<(Effect effect, ShaderBlendMode blendMode)> _activeTileLayerShaders;  // Multiple shaders
```

**Change methods:**
- `GetTileLayerShader()` → `GetTileLayerShaderStack()` (returns list)
- `UpdateLayerShader()` → Select ALL enabled shaders, not just one
- Apply shaders in sequence with blend modes

#### 1.4 Update Rendering Systems

**MapRendererSystem, SpriteRendererSystem:**
- Change from single `Effect?` to `List<Effect>`
- Apply shaders in sequence with blend modes
- Each shader needs its own SpriteBatch.Begin()/End() cycle

**Complexity:** **HIGH** - Requires significant refactoring of rendering pipeline

---

## 2. Advanced Post-Processing (Lines 1829-1833)

### Current Implementation

**Status:** ⚠️ **PARTIALLY IMPLEMENTED** - Single render pass only

**Current Behavior:**
- Single render target for combined layer shader
- One post-processing shader applied
- No depth buffer support
- No multiple render passes
- No screen-space effects

**Location:** `SceneRendererSystem.cs:528-646`

### What's Missing

#### 2.1 Multiple Render Passes

**Current:** One render pass (render to target, apply shader, draw)

**Needed:**
- Support for multiple render passes (e.g., Bloom → Vignette → Chromatic Aberration)
- Each pass renders to intermediate render target
- Pass results feed into next pass

**Required Changes:**
- `RenderTargetManager` needs to support multiple render targets
- `ShaderManagerSystem` needs to return shader stack for combined layer
- `SceneRendererSystem` needs to apply shaders in sequence

#### 2.2 Depth Buffer Support

**Current:** `DepthFormat.None` in render target creation

**Needed:**
- Depth buffer for depth-based effects (depth of field, fog, etc.)
- Depth information passed to shaders

**Required Changes:**
- `RenderTargetManager.GetOrCreateRenderTarget()` needs `DepthFormat` parameter
- Shaders need access to depth buffer
- Depth buffer passed as shader parameter

#### 2.3 Particle Effects Integration

**Current:** Not integrated

**Needed:**
- Particles rendered to same render target as scene
- Particles participate in post-processing
- Particle depth sorting with scene

**Required Changes:**
- Particle system needs to render to scene render target
- Or particles rendered separately and composited

#### 2.4 Screen-Space Effects

**Current:** Not supported

**Needed:**
- Screen-space ambient occlusion (SSAO)
- Screen-space reflections (SSR)
- Screen-space global illumination (SSGI)

**Required Changes:**
- Additional render passes for screen-space calculations
- Depth buffer access (see 2.2)
- Normal buffer (requires additional render target)

**Complexity:** **VERY HIGH** - Requires major rendering pipeline refactoring

---

## 3. Advanced Shader Features (Lines 1796-1800)

### Current Implementation

**Status:** ❌ **NOT IMPLEMENTED**

### 3.1 Shader Parameter Animation Timelines

**Current:** Single animation per parameter

**Needed:**
- Sequence multiple animations (e.g., fade in → hold → fade out)
- Timeline with multiple keyframes
- Animation tracks/channels

**Required Changes:**
- New component: `ShaderParameterTimelineComponent`
- Timeline system that manages multiple animations
- Keyframe interpolation between timeline points

**Complexity:** **MEDIUM** - Extends existing animation system

### 3.2 Shader Parameter Keyframe System

**Current:** Start/End value animation only

**Needed:**
- Multiple keyframes per animation
- Custom interpolation curves
- Keyframe timing control

**Required Changes:**
- Extend `ShaderParameterAnimationComponent` with keyframe array
- Or create new `ShaderParameterKeyframeAnimationComponent`
- Keyframe interpolation logic

**Complexity:** **MEDIUM** - Extends existing animation system

### 3.3 Shader Blending Modes (Multiple Shaders Per Entity)

**Current:** Per-entity shader replaces layer shader

**Needed:**
- Multiple shaders per entity
- Blend modes for entity shaders
- Entity shader composition

**Required Changes:**
- Change `ShaderComponent` to support multiple shaders
- Or create `ShaderStackComponent` for multiple shaders
- Apply shaders in sequence with blend modes

**Complexity:** **HIGH** - Requires refactoring entity shader system

### 3.4 Shader Inheritance/Composition

**Current:** Shaders are independent

**Needed:**
- Shader inheritance (base shader + modifications)
- Shader composition (combine multiple shader effects)
- Shader templates/presets

**Required Changes:**
- Shader definition system with inheritance
- Shader composition logic
- Template/preset system

**Complexity:** **VERY HIGH** - Requires new shader definition system

---

## Implementation Priority

### P0 (Critical - Core Features)
**None** - Current implementation covers basic needs

### P1 (Important - High Value)
1. **Shader Stacking** - Multiple shaders per layer with blend modes
   - **Impact:** HIGH - Enables complex visual effects
   - **Complexity:** HIGH - Requires rendering pipeline refactoring
   - **Effort:** ~2-3 weeks

2. **Multiple Render Passes** - Post-processing chain
   - **Impact:** HIGH - Enables advanced post-processing
   - **Complexity:** HIGH - Requires render target management
   - **Effort:** ~1-2 weeks

### P2 (Nice-to-Have - Future)
3. **Animation Timelines** - Sequence multiple animations
   - **Impact:** MEDIUM - Better animation control
   - **Complexity:** MEDIUM - Extends existing system
   - **Effort:** ~1 week

4. **Keyframe System** - Multiple keyframes per animation
   - **Impact:** MEDIUM - More flexible animations
   - **Complexity:** MEDIUM - Extends existing system
   - **Effort:** ~1 week

5. **Depth Buffer Support** - Depth-based effects
   - **Impact:** MEDIUM - Enables depth effects
   - **Complexity:** MEDIUM - Requires render target changes
   - **Effort:** ~3-5 days

### P3 (Future Enhancements)
6. **Screen-Space Effects** - SSAO, SSR, SSGI
   - **Impact:** HIGH - Advanced visual effects
   - **Complexity:** VERY HIGH - Requires major pipeline work
   - **Effort:** ~1-2 months

7. **Shader Inheritance/Composition** - Shader templates
   - **Impact:** MEDIUM - Better shader organization
   - **Complexity:** VERY HIGH - Requires new system
   - **Effort:** ~1 month

8. **Particle Effects Integration** - Particles in post-processing
   - **Impact:** LOW - Nice visual enhancement
   - **Complexity:** MEDIUM - Requires particle system integration
   - **Effort:** ~1 week

---

## Recommended Implementation Order

1. **Phase 1: Shader Stacking** (P1)
   - Add `ShaderBlendMode` enum
   - Update `LayerShaderComponent` with blend mode
   - Refactor `ShaderManagerSystem` to return shader stack
   - Update rendering systems to apply multiple shaders

2. **Phase 2: Multiple Render Passes** (P1)
   - Extend `RenderTargetManager` for multiple targets
   - Update `SceneRendererSystem` for post-processing chain
   - Test with multiple combined layer shaders

3. **Phase 3: Animation Enhancements** (P2)
   - Add timeline system
   - Add keyframe system
   - Extend animation component

4. **Phase 4: Depth Buffer** (P2)
   - Add depth buffer to render targets
   - Pass depth to shaders
   - Test depth-based effects

5. **Phase 5: Advanced Features** (P3)
   - Screen-space effects
   - Shader inheritance
   - Particle integration

---

## Conclusion

The current shader infrastructure provides a solid foundation but lacks advanced features for complex visual effects. The highest priority items are:

1. **Shader Stacking** - Enables multiple shaders per layer
2. **Multiple Render Passes** - Enables post-processing chains

These two features would unlock most advanced visual effects while maintaining reasonable complexity. The other features can be added incrementally as needed.

**Recommendation:** Start with Shader Stacking (Phase 1) as it provides the most value and enables other features.

