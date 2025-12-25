# P3 Features Analysis: Screen-Space Effects & Shader Inheritance/Composition

**Generated:** 2025-01-27  
**Scope:** Analysis of P3 (Future) features from advanced shader features analysis

---

## Executive Summary

**P3 Features:**
1. **Screen-Space Effects** (~1.5-2 months) - SSAO, SSR, SSGI
2. **Shader Inheritance/Composition** (~1 month) - Shader templates, base shaders, composition

**Current Status:**
- ✅ Foundation exists: Multiple render passes, depth buffer support, shader stacking
- ❌ Screen-space effects: Not implemented (requires additional infrastructure)
- ❌ Shader inheritance/composition: Not implemented (requires shader definition system)

**MonoGame Support:**
- ✅ Vertex/Pixel shaders: Fully supported
- ✅ Multiple render passes: Supported (via multiple SpriteBatch.Begin/End blocks)
- ✅ Depth buffer: Supported (via DepthFormat parameter)
- ❌ Compute shaders: NOT supported (limits some screen-space calculations)
- ❌ Geometry shaders: NOT supported (no procedural geometry)
- ✅ HLSL #include: Supported (for shader inheritance)

---

## 1. Screen-Space Effects

### What Are Screen-Space Effects?

Screen-space effects calculate visual effects based on the rendered screen image (color, depth, normals) rather than 3D scene geometry.

**Common Examples:**
- **SSAO (Screen-Space Ambient Occlusion)**: Darkens areas where objects are close together
- **SSR (Screen-Space Reflections)**: Reflects screen content (not true reflections)
- **SSGI (Screen-Space Global Illumination)**: Approximates indirect lighting
- **Depth of Field**: Blurs based on depth
- **Motion Blur**: Blurs based on velocity
- **Screen-Space Fog**: Fog based on depth

### Current Foundation

**✅ What We Have:**
- Multiple render passes support (via `ShaderRendererSystem`)
- Render target management (via `RenderTargetManager`)
- Depth buffer support (via `DepthFormat` parameter)
- Shader stacking (multiple shaders per layer)

**❌ What's Missing:**
- Normal buffer (G-buffer component)
- Velocity buffer (for motion blur)
- Screen-space sampling utilities
- Depth buffer access in shaders (needs depth-to-texture rendering)
- G-buffer rendering pass

### Architecture Requirements

#### 1.1 G-Buffer System

**What:** Multiple render targets storing different scene information (color, depth, normals, etc.)

**Required:**
```csharp
// New: GBufferManager
public class GBufferManager : IDisposable
{
    // Render targets for G-buffer
    private RenderTarget2D? _colorBuffer;
    private RenderTarget2D? _depthBuffer;
    private RenderTarget2D? _normalBuffer;
    private RenderTarget2D? _velocityBuffer; // For motion blur
    
    // Create G-buffer with all required buffers
    public void CreateGBuffer(int width, int height);
    
    // Get specific buffer
    public RenderTarget2D? GetColorBuffer();
    public RenderTarget2D? GetDepthBuffer();
    public RenderTarget2D? GetNormalBuffer();
    public RenderTarget2D? GetVelocityBuffer();
}
```

**Complexity:** **HIGH** - Requires new system and rendering pipeline changes

#### 1.2 Normal Buffer Rendering

**Problem:** 2D games don't have normals by default (no 3D geometry)

**Solutions:**
- Option A: Generate normals from depth buffer (depth gradients)
- Option B: Store normal information in sprite/tile data (requires data changes)
- Option C: Skip normal buffer, use depth-only effects

**Recommendation:** Start with depth-only effects (SSAO without normals, depth-based fog)

**Complexity:** **MEDIUM-HIGH** - Depends on approach

#### 1.3 Depth Buffer Access in Shaders

**Current:** Depth buffer exists but not accessible as texture in shaders

**Required:**
- Render depth to separate texture
- Pass depth texture to shaders
- Shader samples depth for calculations

**Implementation:**
```csharp
// In rendering system
var depthTexture = RenderDepthToTexture(renderTarget);
shader.Parameters["DepthTexture"].SetValue(depthTexture);
```

**Complexity:** **MEDIUM** - Requires depth-to-texture rendering pass

#### 1.4 Screen-Space Sampling

**Required:** Utility functions for screen-space sampling (sample nearby pixels)

**Shader Code:**
```hlsl
// Sample depth at offset
float SampleDepth(float2 uv, float2 offset)
{
    return tex2D(DepthTexture, uv + offset).r;
}

// Calculate occlusion (SSAO)
float CalculateOcclusion(float2 uv, float depth)
{
    float occlusion = 0.0;
    for (int i = 0; i < SAMPLE_COUNT; i++)
    {
        float2 offset = SAMPLE_OFFSETS[i] * SCALE;
        float sampleDepth = SampleDepth(uv, offset);
        if (sampleDepth < depth - BIAS)
        {
            occlusion += 1.0;
        }
    }
    return occlusion / SAMPLE_COUNT;
}
```

**Complexity:** **LOW** - Shader code only

### Implementation Plan

#### Phase 1: Depth-Based Effects (2-3 weeks)
1. Add depth-to-texture rendering
2. Pass depth texture to shaders
3. Implement depth-based fog
4. Implement simple SSAO (depth-only, no normals)

#### Phase 2: G-Buffer System (2-3 weeks)
1. Create `GBufferManager`
2. Render to multiple buffers (color, depth, normals)
3. Update rendering systems to write to G-buffer
4. Pass G-buffer textures to post-processing shaders

#### Phase 3: Advanced Effects (3-4 weeks)
1. Implement full SSAO (with normals)
2. Implement SSR (screen-space reflections)
3. Implement SSGI (screen-space global illumination)
4. Performance optimization

**Total Estimated Time:** ~1.5-2 months

### MonoGame Considerations

**Supported:**
- ✅ Multiple render targets (manual management)
- ✅ Depth buffer via `DepthFormat` parameter
- ✅ Pixel shaders for screen-space calculations
- ✅ Texture sampling in shaders

**Limitations:**
- ❌ No compute shaders (must use pixel shaders for all calculations)
- ❌ No geometry shaders (no procedural geometry generation)
- ❌ No built-in G-buffer support (must manually manage)
- ⚠️ SpriteBatch doesn't write to depth/normal buffers (need custom rendering)
- ⚠️ Depth buffer access requires depth-to-texture rendering pass

**Solutions:**
- Use custom rendering for G-buffer (not SpriteBatch) or hybrid approach
- Use SpriteBatch for color, custom rendering for depth/normals
- Depth-to-texture requires additional render pass
- All screen-space calculations must be done in pixel shaders (no compute shader fallback)

### Architecture Impact

**New Systems Required:**
- `GBufferManager` - Manages G-buffer render targets
- `GBufferRendererSystem` - Renders scene to G-buffer
- `ScreenSpaceEffectSystem` - Applies screen-space effects

**Modified Systems:**
- `MapRendererSystem` - Write to G-buffer (if using G-buffer)
- `SpriteRendererSystem` - Write to G-buffer (if using G-buffer)
- `SceneRendererSystem` - Apply screen-space effects after G-buffer rendering

**Performance Impact:**
- Multiple render targets = memory overhead
- Additional render passes = performance cost
- Screen-space sampling = GPU computation cost
- Should be optional/quality setting

---

## 2. Shader Inheritance/Composition

### What Is Shader Inheritance/Composition?

**Shader Inheritance:** Base shader with modifications/extensions
- Example: `BaseColorGrading` → `SepiaColorGrading` (inherits base, adds sepia)
- Example: `BaseOutline` → `GlowOutline` (inherits base, adds glow)

**Shader Composition:** Combine multiple shader effects into one
- Example: Combine `Bloom` + `Vignette` + `ChromaticAberration` into single shader
- Example: Combine `Outline` + `Glow` + `ColorTint` into single shader

**Shader Templates/Presets:** Pre-configured shader combinations
- Example: `NightTimePreset` = `Darken` + `BlueTint` + `Vignette`
- Example: `RainPreset` = `Desaturate` + `Blur` + `Distortion`

### Current Foundation

**✅ What We Have:**
- Shader loading via `ShaderService`
- Shader caching
- Shader parameter system
- Shader stacking (multiple shaders applied in sequence)

**❌ What's Missing:**
- Shader definition system (metadata about shaders)
- Shader inheritance mechanism
- Shader composition logic
- Template/preset system
- Shader metadata (parameters, dependencies, etc.)

### Architecture Requirements

#### 2.1 Shader Definition System

**What:** Metadata about shaders (parameters, inheritance, composition rules)

**Required:**
```csharp
// New: ShaderDefinition
public class ShaderDefinition
{
    public string ShaderId { get; set; }
    public string BaseShaderId { get; set; } // For inheritance
    public List<string> ComposedShaderIds { get; set; } // For composition
    public Dictionary<string, ShaderParameterDefinition> Parameters { get; set; }
    public ShaderLayer Layer { get; set; }
    public string? Description { get; set; }
}

public class ShaderParameterDefinition
{
    public string Name { get; set; }
    public Type Type { get; set; }
    public object? DefaultValue { get; set; }
    public object? MinValue { get; set; }
    public object? MaxValue { get; set; }
    public string? Description { get; set; }
}

// New: ShaderDefinitionRegistry
public class ShaderDefinitionRegistry
{
    private readonly Dictionary<string, ShaderDefinition> _definitions = new();
    
    public void Register(ShaderDefinition definition);
    public ShaderDefinition? GetDefinition(string shaderId);
    public List<ShaderDefinition> GetInheritedShaders(string baseShaderId);
}
```

**Complexity:** **MEDIUM** - New system but straightforward

#### 2.2 Shader Inheritance

**Approach A: HLSL Include System**
- Base shader defines functions/techniques
- Inherited shader includes base and extends
- MonoGame supports `#include` in HLSL

**Example:**
```hlsl
// BaseColorGrading.fx
float4 ApplyColorGrading(float4 color, float intensity)
{
    // Base color grading logic
    return color;
}

// SepiaColorGrading.fx
#include "BaseColorGrading.fx"

float4 ApplySepia(float4 color)
{
    // Sepia-specific logic
    color = ApplyColorGrading(color, 1.0); // Use base function
    // Add sepia tint
    return color;
}
```

**Approach B: Shader Composition at Runtime**
- Load base shader and modifier shader
- Combine shader code at runtime (complex, not recommended)
- Or: Use shader stacking (already implemented!)

**Recommendation:** Use HLSL `#include` for inheritance, shader stacking for composition

**Complexity:** **LOW-MEDIUM** - HLSL includes are straightforward

#### 2.3 Shader Composition

**Current Solution:** Shader stacking already provides composition!

**What We Have:**
- Multiple shaders per layer
- Blend modes for composition
- Render target chain for sequential application

**What's Missing:**
- Shader definition metadata (which shaders compose well)
- Template/preset system (pre-configured combinations)
- Validation (ensure compatible shaders are composed)

**Required:**
```csharp
// New: ShaderTemplate
public class ShaderTemplate
{
    public string TemplateId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public List<ShaderTemplateEntry> Shaders { get; set; }
}

public class ShaderTemplateEntry
{
    public string ShaderId { get; set; }
    public ShaderBlendMode BlendMode { get; set; }
    public int RenderOrder { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
}

// Usage
var nightTimeTemplate = new ShaderTemplate
{
    TemplateId = "NightTime",
    Shaders = new List<ShaderTemplateEntry>
    {
        new() { ShaderId = "Darken", BlendMode = ShaderBlendMode.Replace, RenderOrder = 0 },
        new() { ShaderId = "BlueTint", BlendMode = ShaderBlendMode.Multiply, RenderOrder = 1 },
        new() { ShaderId = "Vignette", BlendMode = ShaderBlendMode.Multiply, RenderOrder = 2 }
    }
};

// Apply template
shaderTemplateSystem.ApplyTemplate("NightTime", ShaderLayer.CombinedLayer);
```

**Complexity:** **MEDIUM** - Template system is straightforward, validation is optional

#### 2.4 Shader Metadata System

**Required:** Store shader metadata (parameters, dependencies, compatibility)

**Implementation:**
```csharp
// Shader metadata in mod.json or separate file
{
    "shaders": {
        "CombinedLayerBloom": {
            "parameters": {
                "Intensity": {
                    "type": "float",
                    "default": 1.0,
                    "min": 0.0,
                    "max": 2.0,
                    "description": "Bloom intensity"
                }
            },
            "compatibleWith": ["CombinedLayerVignette", "CombinedLayerChromaticAberration"],
            "requires": [],
            "inheritsFrom": null
        }
    }
}
```

**Complexity:** **LOW** - JSON metadata, straightforward parsing

### Implementation Plan

#### Phase 1: Shader Definition System (1 week)
1. Create `ShaderDefinition` class
2. Create `ShaderDefinitionRegistry`
3. Load shader definitions from mod.json or separate files
4. Integrate with `ShaderService`

#### Phase 2: Shader Inheritance (HLSL Includes) (1 week)
1. Document HLSL include pattern
2. Create base shader library
3. Create example inherited shaders
4. Update shader loading to handle includes

#### Phase 3: Shader Templates/Presets (1 week)
1. Create `ShaderTemplate` system
2. Create `ShaderTemplateRegistry`
3. Create template application system
4. Create example templates

#### Phase 4: Validation & Metadata (1 week)
1. Add shader compatibility checking
2. Add parameter validation from metadata
3. Add shader dependency resolution
4. Documentation and examples

**Total Estimated Time:** ~1 month

### Architecture Impact

**New Systems Required:**
- `ShaderDefinitionRegistry` - Stores shader metadata
- `ShaderTemplateSystem` - Manages and applies templates
- `ShaderCompatibilityValidator` - Validates shader combinations

**Modified Systems:**
- `ShaderService` - Load shader definitions
- `ShaderManagerSystem` - Use definitions for validation
- Mod system - Load shader definitions from mods

**No Breaking Changes:** All additions are optional/extensions

### MonoGame Considerations

**HLSL Includes:** ✅ **SUPPORTED**
- MonoGame supports `#include` in HLSL shaders
- Include paths relative to shader file
- Content pipeline handles includes automatically
- Standard HLSL feature, fully compatible

**Shader Composition:** ✅ **ALREADY SUPPORTED**
- Already supported via shader stacking (implemented in P1)
- Multiple `SpriteBatch.Begin/End` blocks work perfectly
- No additional MonoGame-specific work needed
- Blend modes work via shader-based blending

**Limitations:**
- No built-in inheritance system (must use HLSL `#include`)
- No automatic shader composition (must manually stack shaders)
- Shader metadata not built-in (must create custom system)

---

## Current Support Assessment

### Screen-Space Effects Support

**Foundation:** ✅ **GOOD** (~40% ready)
- Multiple render passes: ✅ Implemented (via SpriteBatch.Begin/End blocks)
- Depth buffer support: ✅ Implemented (via DepthFormat)
- Render target management: ✅ Implemented
- Shader stacking: ✅ Implemented
- Pixel shaders: ✅ Fully supported by MonoGame

**Missing:**
- G-buffer system: ❌ Not implemented
- Normal buffer: ❌ Not implemented
- Depth-to-texture rendering: ❌ Not implemented
- Screen-space sampling utilities: ❌ Not implemented

**MonoGame Limitations:**
- ❌ No compute shaders (all calculations must be in pixel shaders)
- ⚠️ SpriteBatch doesn't write depth/normals (need custom rendering)

**Readiness:** **~40%** - Good foundation, needs G-buffer infrastructure, limited by MonoGame's pixel-shader-only approach

### Shader Inheritance/Composition Support

**Foundation:** ✅ **EXCELLENT** (~70% ready)
- Shader stacking: ✅ Implemented (provides composition!)
- Blend modes: ✅ Implemented
- Multiple shaders per layer: ✅ Implemented
- HLSL #include: ✅ Supported by MonoGame

**Missing:**
- Shader definition system: ❌ Not implemented
- Shader metadata: ❌ Not implemented
- Template/preset system: ❌ Not implemented
- HLSL include documentation: ❌ Not documented

**MonoGame Support:**
- ✅ HLSL #include fully supported (for inheritance)
- ✅ Multiple shader batches fully supported (for composition)
- ✅ Shader parameters fully supported

**Readiness:** **~70%** - Composition works via stacking, needs metadata/templates, HLSL includes work for inheritance

---

## Recommendations

### Screen-Space Effects

**Priority:** **MEDIUM** - Advanced feature, high complexity, MonoGame limitations

**MonoGame Constraints:**
- All calculations must be in pixel shaders (no compute shaders)
- G-buffer requires custom rendering (SpriteBatch doesn't write depth/normals)
- Performance may be lower than engines with compute shader support

**Recommendation:**
1. **Start Simple:** Implement depth-based effects first (depth fog, simple SSAO)
2. **Skip G-Buffer Initially:** Use depth-only approach (no normal buffer)
3. **Add G-Buffer Later:** If full SSAO/SSR/SSGI needed, add G-buffer system
4. **Make Optional:** Screen-space effects should be quality setting (can disable)
5. **Accept Limitations:** Some effects may not be feasible without compute shaders

**Phased Approach:**
- Phase 1: Depth-to-texture + depth-based fog (1 week)
- Phase 2: Simple SSAO (depth-only, pixel shader) (1 week)
- Phase 3: G-buffer system (if needed) (2-3 weeks)
- Phase 4: Full screen-space effects (2-3 weeks, may be limited by pixel-shader-only approach)

**Feasibility:** **MEDIUM** - Possible but may have performance/quality limitations compared to engines with compute shaders

### Shader Inheritance/Composition

**Priority:** **HIGH** - High value, low complexity, fully supported by MonoGame!

**MonoGame Support:**
- ✅ HLSL `#include` fully supported (inheritance)
- ✅ Multiple shader batches fully supported (composition)
- ✅ Shader parameters fully supported

**Recommendation:**
1. **Leverage Existing:** Shader stacking already provides composition
2. **Add Metadata:** Shader definition system for better organization
3. **Add Templates:** Template system for common combinations
4. **Document HLSL Includes:** For inheritance pattern (fully supported!)

**Phased Approach:**
- Phase 1: Shader definition system (1 week)
- Phase 2: Template/preset system (1 week)
- Phase 3: HLSL include examples and documentation (3 days)
- Phase 4: Validation and tooling (3 days)

**Feasibility:** **HIGH** - Fully supported by MonoGame, composition already works!

---

## Implementation Complexity Comparison

| Feature | Complexity | Foundation | MonoGame Support | Estimated Time | Feasibility |
|---------|-----------|------------|------------------|----------------|-------------|
| **Screen-Space Effects** | VERY HIGH | GOOD (40%) | ⚠️ Limited (pixel shaders only) | 1.5-2 months | MEDIUM |
| **Shader Inheritance/Composition** | MEDIUM | EXCELLENT (70%) | ✅ Fully Supported | ~1 month | HIGH |

**Key Insights:**
1. **Shader inheritance/composition:** Much easier because shader stacking already provides composition! Just needs metadata and templates. MonoGame fully supports HLSL `#include` for inheritance.
2. **Screen-space effects:** Feasible but limited by MonoGame's pixel-shader-only approach. No compute shaders means all calculations must be in pixel shaders, which may impact performance for complex effects.

---

## Conclusion

### Screen-Space Effects
- **Status:** Foundation exists (~40%), needs G-buffer infrastructure
- **MonoGame Support:** ⚠️ Limited - Pixel shaders only, no compute shaders
- **Recommendation:** Start with depth-only effects, add G-buffer if needed
- **Timeline:** 1.5-2 months for full implementation
- **Feasibility:** MEDIUM - Possible but may have performance/quality limitations
- **Limitations:** Complex screen-space calculations may be slower without compute shaders

### Shader Inheritance/Composition
- **Status:** Composition already works via shader stacking! (~70% ready)
- **MonoGame Support:** ✅ Fully Supported - HLSL `#include` and multiple batches work
- **Recommendation:** Add metadata and template system
- **Timeline:** ~1 month for full implementation
- **Feasibility:** HIGH - Fully supported, composition already works!

**Next Steps:**
1. **Screen-space effects:** Start with depth-to-texture + simple effects (depth fog, basic SSAO)
2. **Shader organization:** Add shader definition and template systems (high value, low risk)
3. Both can be implemented independently (no dependencies)

**Priority Recommendation:**
- **Shader Inheritance/Composition** should be implemented first (easier, fully supported, high value)
- **Screen-Space Effects** can follow if needed (more complex, limited by MonoGame capabilities)
