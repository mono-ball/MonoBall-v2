# MonoGame Shader Support Analysis

**Source:** [MonoGame Shader Documentation](https://docs.monogame.net/articles/tutorials/building_2d_games/24_shaders/index.html)  
**Generated:** 2025-01-27

---

## ✅ Supported Features

### Shader Types

1. **Vertex Shaders** ✅ **SUPPORTED**
   - Process vertices (corners of shapes)
   - Can manipulate positions for effects (waves, distortion)
   - Even in 2D, sprites are quads with 4 vertices
   - **Note:** SpriteBatch handles most vertex shader work automatically

2. **Pixel Shaders** ✅ **SUPPORTED**
   - Determine final color of each pixel
   - Primary focus for 2D visual effects
   - Used for: color adjustments, filters, transitions, lighting

### Shader Languages

1. **HLSL (High-Level Shader Language)** ✅ **SUPPORTED**
   - Primary shader language for MonoGame
   - C-like syntax
   - Developed by Microsoft for DirectX

2. **GLSL Translation** ✅ **SUPPORTED** (via MojoShader)
   - HLSL automatically translated to GLSL for OpenGL platforms
   - Translation happens during content build process
   - No manual GLSL writing needed

### Shader Models (Profiles)

**DirectX Platforms:**
- ✅ `vs_4_0_level_9_1` / `ps_4_0_level_9_1` (Recommended for compatibility)
- ✅ `vs_4_0_level_9_3` / `ps_4_0_level_9_3`
- ✅ `vs_4_0` / `ps_4_0`
- ✅ `vs_4_1` / `ps_4_1`
- ✅ `vs_5_0` / `ps_5_0`

**OpenGL Platforms:**
- ✅ `vs_2_0` / `ps_2_0`
- ✅ `vs_3_0` / `ps_3_0` (Recommended for compatibility)

**Recommendation:** Use `vs_4_0_level_9_1`/`ps_4_0_level_9_1` for DirectX, `vs_3_0`/`ps_3_0` for OpenGL for maximum compatibility.

### Content Pipeline

- ✅ `.fx` files supported
- ✅ ContentManager loading (`content.Load<Effect>("path")`)
- ✅ Automatic compilation via MGCB Editor
- ✅ MojoShader translation for cross-platform

### SpriteBatch Integration

1. **One Effect Per Batch** ✅ **SUPPORTED**
   - One effect can be applied per `SpriteBatch.Begin()`/`End()` block
   - All draw calls in that block use the same effect

2. **Multiple Effects** ✅ **SUPPORTED**
   - Use multiple `Begin()`/`End()` blocks for different effects
   - Each block can have a different effect

3. **Selective Effect Application** ✅ **SUPPORTED**
   - Use `Begin()` with effect for some sprites
   - Use `Begin()` without effect for other sprites

4. **SpriteSortMode.Immediate** ✅ **SUPPORTED** (with caveats)
   - Allows parameter changes between draw calls
   - **Warning:** Disables batching, causes performance issues
   - Should only be used when absolutely necessary

### Shader Parameters

- ✅ Runtime parameter control
- ✅ Parameter types: float, Vector2, Vector3, Vector4, Color, Texture2D, Matrix
- ✅ Parameters must be set each frame (shaders are stateless)

---

## ❌ NOT Supported

### Advanced Shader Types

1. **Compute Shaders** ❌ **NOT SUPPORTED**
   - Explicitly stated in documentation
   - Would enable physics simulations, GPU compute

2. **Geometry Shaders** ❌ **NOT SUPPORTED**
   - Explicitly stated in documentation
   - Would enable procedural geometry generation

3. **Hull/Domain Shaders** ❌ **NOT SUPPORTED**
   - Explicitly stated in documentation
   - Would enable tessellation

**Note:** Documentation states these may be added in future MonoGame versions as the graphics pipeline evolves.

### Future Features

- **Vulkan Support** ⚠️ **PLANNED** (not yet available)
  - Documentation mentions MonoGame is planning to upgrade to Vulkan
  - Will enhance graphical capabilities and shader support

- **DirectX 12 Support** ⚠️ **PLANNED** (not yet available)
  - Part of planned graphics pipeline upgrade
  - Will enable more advanced visual effects

---

## ⚠️ Limitations & Important Considerations

### SpriteBatch Parameter Timing

**Critical Limitation:**
- Effect parameters are applied during `SpriteBatch.End()`, not during draw calls
- Changing parameters between `Draw()` calls: **Only last value is used**
- **Exception:** `SpriteSortMode.Immediate` applies parameters immediately (but disables batching)

**Example (from documentation):**
```csharp
spriteBatch.Begin(effect: exampleEffect);
exampleEffect.Parameters["Param"].SetValue(1.0f);
spriteBatch.Draw(texture1, position, color);
exampleEffect.Parameters["Param"].SetValue(0.5f);
spriteBatch.Draw(texture2, position, color);
spriteBatch.End();
// Both draw calls use 0.5f (last value), not 1.0f and 0.5f!
```

**Solution:** Use `SpriteSortMode.Immediate` (performance cost) or separate `Begin()`/`End()` blocks.

### 2D Game Considerations

- **Vertex Shaders:** Less commonly used in 2D (SpriteBatch handles most vertex work)
- **Pixel Shaders:** Primary focus for 2D visual effects
- **Sprites are Quads:** Even 2D sprites are 3D quads (two triangles), so vertices exist

### Cross-Platform Considerations

- **Shader Model Compatibility:** Must target compatible shader models
- **MojoShader Translation:** Automatic but may have limitations
- **Platform-Specific Code:** Use `#if OPENGL` / `#else` for platform-specific shader code

---

## Impact on Our Implementation

### ✅ What Works with Our Current Implementation

1. **Shader Stacking:** ✅ Fully supported
   - Multiple `Begin()`/`End()` blocks work perfectly
   - Each shader in stack can be applied in separate batch

2. **Blend Modes:** ✅ Supported (via shader code)
   - Shader-based blending works (not BlendState)
   - Our implementation is correct

3. **Multiple Render Passes:** ✅ Supported
   - Render to target, apply shader, render to next target
   - Our implementation is correct

4. **Depth Buffer:** ✅ Supported
   - `DepthFormat` parameter in RenderTarget2D
   - Our implementation is correct

### ⚠️ Considerations for Our Implementation

1. **Parameter Changes Between Draw Calls:**
   - Our implementation uses `SpriteSortMode.Immediate` in some places
   - This is correct for our use case (shader stacking requires immediate mode)
   - Performance cost is acceptable for shader effects

2. **Shader Statelessness:**
   - We correctly set parameters each frame
   - Our dirty tracking helps but we still set parameters (correct approach)

3. **Multiple Effects:**
   - Our shader stacking uses multiple `Begin()`/`End()` blocks
   - This is the correct MonoGame pattern

### ❌ What We Can't Do (MonoGame Limitations)

1. **Compute Shaders:** Cannot use for GPU compute
2. **Geometry Shaders:** Cannot generate geometry procedurally
3. **Advanced Shader Types:** Limited to vertex/pixel shaders

---

## Recommendations

### For Screen-Space Effects

**✅ Supported:**
- Depth buffer rendering
- Multiple render passes
- Pixel shader effects
- Texture sampling

**⚠️ Limitations:**
- No compute shaders (would need CPU fallback for some calculations)
- Must use pixel shaders for all effects
- G-buffer requires manual management (no built-in support)

**Conclusion:** Screen-space effects are feasible but require manual G-buffer management and pixel shader implementation.

### For Shader Inheritance/Composition

**✅ Supported:**
- HLSL `#include` (standard HLSL feature, should work)
- Multiple shaders via stacking (already implemented)
- Shader parameters

**⚠️ Considerations:**
- `#include` paths must be correct
- Content pipeline must handle includes
- No built-in inheritance system (must use HLSL includes)

**Conclusion:** Shader inheritance via HLSL `#include` should work. Composition already works via our shader stacking.

---

## Summary

**MonoGame Supports:**
- ✅ Vertex and Pixel shaders
- ✅ HLSL with automatic GLSL translation
- ✅ Multiple shader models (vs_4_0_level_9_1 recommended)
- ✅ SpriteBatch integration (one effect per batch)
- ✅ Runtime parameter control
- ✅ Multiple render passes (via multiple batches)

**MonoGame Does NOT Support:**
- ❌ Compute shaders
- ❌ Geometry shaders
- ❌ Hull/domain shaders
- ❌ Vulkan (yet - planned)
- ❌ DirectX 12 (yet - planned)

**Our Implementation:**
- ✅ Fully compatible with MonoGame capabilities
- ✅ Uses supported features correctly
- ✅ No reliance on unsupported features

**References:**
- [MonoGame Shader Tutorial](https://docs.monogame.net/articles/tutorials/building_2d_games/24_shaders/index.html)

