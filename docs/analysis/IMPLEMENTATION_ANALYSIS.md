# Advanced Shader Features Implementation Analysis

**Generated:** 2025-01-27  
**Scope:** Analysis of P1/P2 shader features implementation

---

## Critical Issues

### 1. Memory Leak: Keyframes Not Cleaned Up on Entity Destruction

**Location:** `ShaderParameterTimelineSystem.cs`

**Problem:**
- Keyframes are stored in `_keyframes` dictionary per entity
- When entities are destroyed or components removed, keyframes are never cleaned up
- Dictionary grows indefinitely, causing memory leak

**Impact:** **CRITICAL** - Memory leak over time

**Fix Required:**
- Subscribe to entity destruction events or component removal
- Call `RemoveKeyframes()` when component is removed
- Or: Use entity lifecycle hooks if available in Arch ECS

**Code:**
```csharp
// Missing cleanup when component removed
// Need to detect when ShaderParameterTimelineComponent is removed
```

---

### 2. DRY Violation: Duplicate UpdateTimeline Methods

**Location:** `ShaderParameterTimelineSystem.cs` (lines 136-232, 234-330)

**Problem:**
- Two nearly identical `UpdateTimeline()` methods:
  - One for `LayerShaderComponent` (lines 136-232)
  - One for `ShaderComponent` (lines 234-330)
- ~95% of code is duplicated
- Violates DRY principle

**Impact:** **HIGH** - Maintenance burden, bug risk

**Fix Required:**
- Extract common logic to shared method
- Use generics or interface to handle both component types
- Or: Use a single method that accepts both component types via interface/abstraction

**Example Fix:**
```csharp
private void UpdateTimeline<T>(
    ref ShaderParameterTimelineComponent timeline,
    ref T shader,
    float deltaTime,
    Entity entity,
    ShaderLayer layer,
    string shaderId
) where T : struct, IShaderComponent
```

---

### 3. Unused Field: _world in ShaderRendererSystem

**Location:** `ShaderRendererSystem.cs` (line 18)

**Problem:**
- `_world` field is declared but never used
- Dead code, unnecessary dependency

**Impact:** **LOW** - Code cleanliness

**Fix Required:**
- Remove `_world` field and parameter from constructor

---

## Architecture Issues

### 4. Inefficient GetRenderTargets() Calls

**Location:** `ShaderRendererSystem.cs` (lines 168-171, 212-215), `MapRendererSystem.cs` (lines 276-280), `SpriteRendererSystem.cs` (lines 348-352)

**Problem:**
- `GetRenderTargets()` is called multiple times in same method
- Returns array, which is allocated each time
- Pattern: `graphicsDevice.GetRenderTargets().Length > 0` then `graphicsDevice.GetRenderTargets()[0]`

**Impact:** **MEDIUM** - Performance (unnecessary allocations)

**Fix Required:**
- Cache result: `var renderTargets = graphicsDevice.GetRenderTargets();`
- Check length and access element from cached array

**Example:**
```csharp
var renderTargets = graphicsDevice.GetRenderTargets();
RenderTarget2D? previousTarget = renderTargets.Length > 0
    ? renderTargets[0].RenderTarget as RenderTarget2D
    : null;
```

---

### 5. Potential Null Reference: GetRenderTargets() Array Access

**Location:** `ShaderRendererSystem.cs`, `MapRendererSystem.cs`, `SpriteRendererSystem.cs`

**Problem:**
- Code checks `Length > 0` then accesses `[0]`, but array could theoretically be empty between checks (unlikely but not impossible)
- More importantly: `GetRenderTargets()` might throw if graphics device is in invalid state

**Impact:** **LOW** - Edge case, but should be defensive

**Fix Required:**
- Use cached array (fixes both issues)
- Add try-catch if needed for robustness

---

### 6. Shader Stacking Logic Issue: previousOutput Tracking

**Location:** `ShaderRendererSystem.cs` (lines 79-132)

**Problem:**
- For first shader in stack, `previousOutput` is correctly null
- For subsequent shaders, `previousOutput = currentSource` is set AFTER rendering
- But `currentSource` is the INPUT to the current shader, not the OUTPUT
- Logic should be: `previousOutput` should be the result of the PREVIOUS shader pass

**Current Logic:**
```csharp
// After rendering shader i:
previousOutput = currentSource; // This is the INPUT to shader i, not the OUTPUT
currentSource = nextTarget; // This is the OUTPUT of shader i
```

**Correct Logic:**
```csharp
// After rendering shader i:
previousOutput = nextTarget; // OUTPUT of shader i (for next shader's blend mode)
currentSource = nextTarget; // INPUT for next shader
```

**Impact:** **HIGH** - Blend modes won't work correctly for shaders after the first

**Fix Required:**
- Fix `previousOutput` assignment to use `nextTarget` (output) instead of `currentSource` (input)

---

## SOLID/DRY Issues

### 7. Code Duplication: Render Target Management

**Location:** `MapRendererSystem.cs` (lines 276-280), `SpriteRendererSystem.cs` (lines 348-352), `ShaderRendererSystem.cs` (lines 168-171, 212-215)

**Problem:**
- Same pattern for getting previous render target repeated in 3+ places
- Violates DRY principle

**Impact:** **MEDIUM** - Maintenance burden

**Fix Required:**
- Extract to utility method: `GetCurrentRenderTarget(GraphicsDevice)`
- Or: Add to `RenderTargetManager` as helper method

---

### 8. Code Duplication: Shader Stacking Setup

**Location:** `MapRendererSystem.cs` (lines 262-339), `SpriteRendererSystem.cs` (lines 334-470)

**Problem:**
- Similar logic for:
  - Checking if shader stacking needed
  - Getting render target
  - Saving/restoring previous render target
  - Clearing render target
  - Applying shader stack

**Impact:** **MEDIUM** - Maintenance burden

**Fix Required:**
- Extract common logic to `ShaderRendererSystem` or helper method
- Or: Create `RenderToTargetWithShaderStack()` method

---

## MonoGame Bad Practices

### 9. Multiple SpriteBatch.Begin/End Calls in Shader Stacking

**Location:** `ShaderRendererSystem.cs` (lines 178-189, 221-232)

**Problem:**
- Each shader in stack requires separate `SpriteBatch.Begin/End` cycle
- For 3 shaders = 3 Begin/End cycles
- Performance overhead (state changes, validation)

**Impact:** **MEDIUM** - Performance degradation with many shaders

**Note:** This is unavoidable for shader stacking (each shader needs separate pass), but should be documented

**Fix Required:**
- Document performance implications
- Consider batching optimizations if possible

---

### 10. Render Target Not Cleared Before Drawing

**Location:** `ShaderRendererSystem.cs` (line 175), `MapRendererSystem.cs` (line 285), `SpriteRendererSystem.cs` (line 357)

**Problem:**
- Some places clear render target (`_graphicsDevice.Clear(Color.Transparent)`)
- Some places don't (in `ShaderRendererSystem.RenderWithShader`)
- Inconsistent behavior

**Impact:** **LOW** - Visual artifacts if render target has stale data

**Fix Required:**
- Always clear render target before drawing (or document when it's safe to skip)
- Add `graphicsDevice.Clear()` in `RenderWithShader` before drawing

---

### 11. Exception Handling in ApplyBlendMode Swallows Errors

**Location:** `ShaderRendererSystem.cs` (lines 261-285)

**Problem:**
- `ApplyBlendMode()` catches all exceptions and logs warning
- If shader doesn't support blend mode, silently falls back to Replace
- No way for caller to know blend mode failed

**Impact:** **MEDIUM** - Silent failures, hard to debug

**Fix Required:**
- Consider returning bool to indicate success/failure
- Or: Throw exception for unsupported blend modes (fail fast per .cursorrules)
- Or: Validate shader supports blend mode before applying

---

## Bugs

### 12. Shader Stacking: First Shader Applied Twice

**Location:** `MapRendererSystem.cs` (lines 288-306), `SpriteRendererSystem.cs` (lines 360-363)

**Problem:**
- When shader stacking is needed, first shader is applied during geometry rendering (if Replace mode)
- Then `ApplyShaderStack()` is called with the FULL stack (including first shader)
- First shader gets applied twice

**Example:**
```csharp
// Render geometry with first shader (if Replace)
Effect? firstShader = shaderStack[0].blendMode == ShaderBlendMode.Replace 
    ? shaderStack[0].effect 
    : null;
// ... render with firstShader ...

// Then apply FULL stack (including first shader again!)
_shaderRendererSystem.ApplyShaderStack(renderTarget, null, shaderStack, ...);
```

**Impact:** **HIGH** - Incorrect rendering, performance waste

**Fix Required:**
- Don't apply first shader during geometry rendering when stacking
- Or: Pass stack without first shader to `ApplyShaderStack()` if first shader was already applied
- Or: Always render geometry without shaders when stacking, let `ApplyShaderStack()` handle all shaders

---

### 13. Timeline Duration Not Updated When Keyframes Change

**Location:** `ShaderParameterTimelineSystem.cs` (line 123)

**Problem:**
- `AddKeyframes()` updates duration in component
- But if keyframes are modified after component is created, duration might be stale
- No validation that duration matches actual keyframe times

**Impact:** **LOW** - Edge case, but could cause issues

**Fix Required:**
- Recalculate duration when keyframes are accessed if needed
- Or: Validate duration matches max keyframe time

---

## Recommendations Summary

### Critical (Must Fix)
1. **Memory leak:** Clean up keyframes on entity destruction
2. **Shader stacking bug:** Fix `previousOutput` tracking logic
3. **Double shader application:** Fix first shader being applied twice

### High Priority
4. **DRY violation:** Consolidate duplicate `UpdateTimeline()` methods
5. **Performance:** Cache `GetRenderTargets()` results

### Medium Priority
6. **Code duplication:** Extract render target management to utility
7. **Code duplication:** Extract shader stacking setup logic
8. **Exception handling:** Improve blend mode error handling
9. **Render target clearing:** Ensure consistent clearing behavior

### Low Priority
10. **Unused field:** Remove `_world` from `ShaderRendererSystem`
11. **Documentation:** Document performance implications of shader stacking

---

## Testing Recommendations

1. **Memory leak test:** Create/destroy entities with timeline components, verify keyframes dictionary doesn't grow
2. **Shader stacking test:** Verify blend modes work correctly with multiple shaders
3. **Performance test:** Measure frame time with 1, 2, 3+ shaders in stack
4. **Edge case test:** Test with empty shader stack, single shader, multiple shaders with different blend modes

