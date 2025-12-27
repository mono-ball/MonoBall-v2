# Shader Updates Analysis

**Generated:** 2025-01-27  
**Scope:** Complete analysis of shader updates for architecture issues, Arch ECS/event issues, SOLID/DRY violations, bugs, and inconsistencies with other mod resource loading

---

## Executive Summary

The shader system implementation is **generally well-architected** but contains several critical and important issues:

**Critical Issues:**
- ❌ **NO FALLBACK CODE Violation**: `ShaderService.LoadShader()` returns null on failures instead of failing fast
- ❌ **Missing Event Firing**: `ShaderParameterChangedEvent` not fired when parameters change directly (only in animation system)
- ⚠️ **Arch ECS Pattern**: `ShaderManagerSystem` doesn't inherit from `BaseSystem` (intentional but inconsistent)

**Important Issues:**
- **DRY Violations**: Duplicate code in multiple places
- **SOLID Violations**: `ShaderManagerSystem` has too many responsibilities
- **Inconsistencies**: Different error handling patterns compared to other resource loaders

---

## 1. Architecture Issues

### 1.1 ❌ CRITICAL: ShaderService Returns Null on Failure (Violates NO FALLBACK CODE Rule)

**Location:** `MonoBall.Core/Rendering/ShaderService.cs` lines 80-142

**Issue:** `LoadShader()` catches exceptions and returns null instead of failing fast, violating the `.cursorrules` "NO FALLBACK CODE" principle.

**Current Code:**
```csharp
public Effect? LoadShader(string shaderId)
{
    // ... validation ...
    
    try
    {
        return _shaderLoader.LoadShader(shaderDef, modManifest);
    }
    catch (Exception ex)
    {
        _logger.Error(
            ex,
            "Failed to load shader: {ShaderId} from mod {ModId}",
            shaderId,
            modManifest.Id
        );
        return null; // ❌ Fallback code - silently degrades
    }
}
```

**Problem:** Per `.cursorrules`: "NEVER introduce fallback code - code should fail fast with clear errors rather than silently degrade". Returning null is silent degradation.

**Impact:** **CRITICAL** - Violates core project principle

**Fix Required:**
```csharp
public Effect LoadShader(string shaderId)
{
    // ... validation ...
    
    // Remove try-catch, let exceptions propagate
    return _shaderLoader.LoadShader(shaderDef, modManifest);
}
```

**Note:** `GetShader()` already handles null from `LoadShader()` gracefully, but this violates the fail-fast principle. If shader loading failures should be recoverable, document that null is expected and ensure all callers handle it properly.

---

### 1.2 ⚠️ ShaderManagerSystem Not Following Arch ECS Pattern

**Location:** `MonoBall.Core/ECS/Systems/ShaderManagerSystem.cs`

**Issue:** `ShaderManagerSystem` doesn't inherit from `BaseSystem<World, float>` but has ECS dependencies and uses queries. The class comment acknowledges this is intentional for render-phase timing.

**Current:**
```csharp
/// <summary>
/// System that manages shader effects and updates their parameters.
/// Shader state is updated in Render phase to avoid timing mismatches.
/// Note: This is not an Arch ECS BaseSystem (no Update() method) - it's a Render-phase helper
/// that is called explicitly from SceneRendererSystem.
/// </summary>
public class ShaderManagerSystem
{
    private readonly World _world;
    private readonly QueryDescription _layerShaderQuery;
    // ...
    public void UpdateShaderState() { } // Called manually
}
```

**Problem:** 
- Inconsistent with other ECS systems (all others inherit from `BaseSystem`)
- Manual invocation pattern is error-prone (must remember to call `UpdateShaderState()`)
- Uses ECS queries but not integrated with Arch ECS `Group<T>` system

**Impact:** **MEDIUM** - Architectural inconsistency, harder to maintain

**Recommendation:** 
- **Option 1 (Current)**: Keep as-is but document clearly why it's not a BaseSystem (Render-phase timing)
- **Option 2**: Make it a BaseSystem with empty `Update()` and keep `UpdateShaderState()` for Render phase
- **Option 3**: Split into two systems: `ShaderManagerSystem` (Update phase) and `ShaderRenderSystem` (Render phase)

**Status:** Documented but inconsistent with codebase patterns

---

### 1.3 ⚠️ ShaderManagerSystem Continues on Shader Load Failure

**Location:** `MonoBall.Core/ECS/Systems/ShaderManagerSystem.cs` lines 454-463

**Issue:** When a shader fails to load, the system logs a warning and continues with other shaders. This is fallback behavior.

**Current Code:**
```csharp
Effect? effect = _shaderService.GetShader(shaderComp.ShaderId);
if (effect == null)
{
    _logger.Warning(
        "Shader {ShaderId} failed to load for layer {Layer}",
        shaderComp.ShaderId,
        layer
    );
    continue; // Skip failed shader, continue with others
}
```

**Problem:** This is acceptable fallback behavior IF shaders are optional. However, if a shader is required, this silently degrades.

**Impact:** **MEDIUM** - May hide critical shader loading failures

**Recommendation:** 
- If shaders are optional: Document that failed shaders are skipped
- If shaders are required: Throw exception instead of continuing

**Status:** Needs clarification on whether shaders are optional or required

---

## 2. Arch ECS / Event Issues

### 2.1 ❌ CRITICAL: ShaderParameterChangedEvent Not Fired on Direct Parameter Changes

**Location:** `MonoBall.Core/ECS/Systems/ShaderManagerSystem.cs` lines 530-646

**Issue:** `UpdateShaderParametersForEntity()` updates shader parameters but does NOT fire `ShaderParameterChangedEvent`. The event is only fired in `ShaderParameterAnimationSystem`.

**Current Code:**
```csharp
private void UpdateShaderParametersForEntity(Entity entity, Effect effect)
{
    // ... parameter validation and application ...
    
    // Apply parameter
    ApplyShaderParameter(effect, paramName, value);
    
    // Update previous value for dirty tracking
    previousValues[paramName] = value;
    
    // ❌ Missing: EventBus.Send(ref evt);
}
```

**Problem:** 
- Parameters changed directly (not via animation) don't fire events
- Inconsistent with animation system which fires events
- Mods/systems cannot react to direct parameter changes

**Impact:** **CRITICAL** - Missing event-driven behavior, inconsistent with animation system

**Fix Required:**
```csharp
private void UpdateShaderParametersForEntity(Entity entity, Effect effect)
{
    // ... existing code ...
    
    // Get old value for event
    object? oldValue = previousValues.TryGetValue(paramName, out var existingValue)
        ? existingValue
        : null;
    
    // Apply parameter
    ApplyShaderParameter(effect, paramName, value);
    
    // Update previous value for dirty tracking
    previousValues[paramName] = value;
    
    // Fire event
    ref var shader = ref _world.Get<LayerShaderComponent>(entity);
    var evt = new ShaderParameterChangedEvent
    {
        Layer = shader.Layer,
        ShaderId = shader.ShaderId,
        ParameterName = paramName,
        OldValue = oldValue,
        NewValue = value,
        ShaderEntity = entity,
    };
    EventBus.Send(ref evt);
}
```

---

### 2.2 ✅ Event Subscription Pattern (Codebase-Wide)

**Location:** All event files

**Status:** ✅ **ACCEPTABLE** - Events are fired but not subscribed. This matches a codebase-wide pattern.

**Analysis:**
- **42 `EventBus.Send()` calls** across 19 files
- **0 `EventBus.Subscribe()` calls** found in entire codebase
- **48 event files** defined
- `EventBus.Send()` returns early if no subscribers (optimized)

**Note:** This is a codebase-wide pattern, not a shader-specific issue. Events are fired for potential future use, mod integration, debugging, or logging, but not actively subscribed to. The EventBus implementation is optimized to return early if there are no subscribers, so this pattern has no performance cost.

---

## 3. SOLID / DRY Violations

### 3.1 ❌ DRY Violation: Duplicate UpdateAnimation Methods

**Location:** `MonoBall.Core/ECS/Systems/ShaderParameterAnimationSystem.cs` lines 99-327

**Issue:** Two nearly identical `UpdateAnimation()` methods - one for `ShaderComponent`, one for `LayerShaderComponent`. The only difference is the component type.

**Current Code:**
```csharp
private void UpdateAnimation(
    ref ShaderParameterAnimationComponent animation,
    ref ShaderComponent shader,  // ← First overload
    float deltaTime,
    Entity entity,
    ShaderLayer layer,
    string shaderId
) { /* 113 lines */ }

private void UpdateAnimation(
    ref ShaderParameterAnimationComponent animation,
    ref LayerShaderComponent shader,  // ← Second overload
    float deltaTime,
    Entity entity,
    ShaderLayer layer,
    string shaderId
) { /* 113 lines of duplicate code */ }
```

**Problem:** 
- 226 lines of duplicate code
- Changes must be made in two places
- Violates DRY principle

**Impact:** **MEDIUM** - Maintenance burden, risk of inconsistencies

**Fix Required:** Extract common logic into a shared method:
```csharp
private void UpdateAnimation(
    ref ShaderParameterAnimationComponent animation,
    ref ShaderComponent shader,
    float deltaTime,
    Entity entity,
    ShaderLayer layer,
    string shaderId
)
{
    UpdateAnimationCore(
        ref animation,
        ref shader.Parameters,
        deltaTime,
        entity,
        layer,
        shaderId
    );
}

private void UpdateAnimation(
    ref ShaderParameterAnimationComponent animation,
    ref LayerShaderComponent shader,
    float deltaTime,
    Entity entity,
    ShaderLayer layer,
    string shaderId
)
{
    UpdateAnimationCore(
        ref animation,
        ref shader.Parameters,
        deltaTime,
        entity,
        layer,
        shaderId
    );
}

private void UpdateAnimationCore(
    ref ShaderParameterAnimationComponent animation,
    ref Dictionary<string, object>? parameters,
    float deltaTime,
    Entity entity,
    ShaderLayer layer,
    string shaderId
)
{
    // Common logic here (113 lines)
}
```

---

### 3.2 ❌ DRY Violation: Duplicate ScreenSize Update Code

**Location:** `MonoBall.Core/ECS/Systems/ShaderManagerSystem.cs` lines 238-307

**Issue:** `UpdateAllLayersScreenSize()` has three nearly identical blocks of code for tile, sprite, and combined layers.

**Current Code:**
```csharp
public void UpdateAllLayersScreenSize(int width, int height)
{
    var screenSize = new Vector2(width, height);

    // Update ScreenSize for tile layer shaders
    foreach (var (effect, _, _) in _activeTileLayerShaders)
    {
        try
        {
            ShaderParameterApplier.ApplyParameter(
                effect,
                "ScreenSize",
                screenSize,
                _logger
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.Debug(ex, "ScreenSize parameter not available...");
        }
    }

    // Update ScreenSize for sprite layer shaders
    foreach (var (effect, _, _) in _activeSpriteLayerShaders)
    {
        // ❌ Duplicate code (same 10 lines)
    }

    // Update ScreenSize for combined layer shaders
    foreach (var (effect, _, _) in _activeCombinedLayerShaders)
    {
        // ❌ Duplicate code (same 10 lines)
    }
}
```

**Problem:** 
- 30 lines of duplicate code
- Changes must be made in three places
- Violates DRY principle

**Impact:** **LOW** - Small duplication but easy to fix

**Fix Required:**
```csharp
public void UpdateAllLayersScreenSize(int width, int height)
{
    var screenSize = new Vector2(width, height);
    
    UpdateScreenSizeForShaders(_activeTileLayerShaders, screenSize);
    UpdateScreenSizeForShaders(_activeSpriteLayerShaders, screenSize);
    UpdateScreenSizeForShaders(_activeCombinedLayerShaders, screenSize);
}

private void UpdateScreenSizeForShaders(
    List<(Effect effect, ShaderBlendMode blendMode, Entity entity)> shaders,
    Vector2 screenSize
)
{
    foreach (var (effect, _, _) in shaders)
    {
        try
        {
            ShaderParameterApplier.ApplyParameter(
                effect,
                "ScreenSize",
                screenSize,
                _logger
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.Debug(ex, "ScreenSize parameter not available or invalid");
        }
    }
}
```

---

### 3.3 ⚠️ SOLID Violation: ShaderManagerSystem Has Too Many Responsibilities

**Location:** `MonoBall.Core/ECS/Systems/ShaderManagerSystem.cs`

**Issue:** `ShaderManagerSystem` handles multiple responsibilities:
1. Managing shader stacks (loading, caching)
2. Updating shader parameters
3. Firing events
4. Validating shader compatibility
5. Tracking dirty state
6. Managing ScreenSize parameters

**Problem:** Violates Single Responsibility Principle (SRP)

**Impact:** **MEDIUM** - Large class (803 lines), harder to maintain

**Recommendation:** Consider splitting into:
- `ShaderStackManager` - Manages shader stacks and loading
- `ShaderParameterManager` - Updates parameters and fires events
- `ShaderCompatibilityValidator` - Validates compatibility

**Status:** Works but could be better organized

---

## 4. Bugs

### 4.1 ❌ Missing Event Firing (Already Covered in 2.1)

See section 2.1 for details.

---

### 4.2 ⚠️ ValidateShaderIdFormat Throws Exception But Not Used Consistently

**Location:** `MonoBall.Core/Rendering/ShaderService.cs` lines 53-77

**Issue:** `ValidateShaderIdFormat()` throws `ArgumentException` but is only called in `LoadShader()`, not in `GetShader()` or `HasShader()`.

**Current Code:**
```csharp
public Effect? GetShader(string shaderId)
{
    // ... validation ...
    
    // ❌ Missing: ValidateShaderIdFormat(shaderId);
    
    lock (_lock)
    {
        // ...
    }
}
```

**Problem:** 
- Inconsistent validation
- Invalid shader IDs may pass through to `GetShader()` and `HasShader()`

**Impact:** **LOW** - May cause confusing errors later

**Fix Required:** Add validation to all public methods that accept shader IDs.

---

### 4.3 ⚠️ Missing Error Handling for Invalid Shader Bytecode

**Location:** `MonoBall.Core/Rendering/ShaderLoader.cs` lines 61-78

**Issue:** `LoadShader()` catches generic `Exception` and wraps it, but doesn't handle specific MonoGame shader loading errors.

**Current Code:**
```csharp
try
{
    byte[] bytecode = File.ReadAllBytes(mgfxoPath);
    var effect = new Effect(_graphicsDevice, bytecode);
    return effect;
}
catch (Exception ex)
{
    throw new InvalidOperationException(
        $"Failed to create Effect from bytecode for shader '{shaderDefinition.Id}': {ex.Message}",
        ex
    );
}
```

**Problem:** 
- Generic exception handling loses specific error information
- MonoGame may throw specific exceptions for invalid bytecode

**Impact:** **LOW** - Error messages may be less helpful

**Recommendation:** Catch specific MonoGame exceptions if available, or document that generic exception is acceptable.

---

## 5. Inconsistencies with Other Mod Resource Loading

### 5.1 ⚠️ Different Error Handling Patterns

**Comparison:**

| Service | On Failure | Fallback Behavior |
|---------|-----------|-------------------|
| `ShaderService.LoadShader()` | Returns `null` | ❌ Silent degradation |
| `SpriteLoaderService.GetSpriteTexture()` | Returns `null` | ✅ Uses placeholder texture |
| `TilesetLoaderService.LoadTileset()` | Returns `null` | ✅ No fallback (consistent) |

**Issue:** 
- `ShaderService` returns null (violates NO FALLBACK CODE rule)
- `SpriteLoaderService` uses placeholder texture (fallback behavior)
- `TilesetLoaderService` returns null (consistent with shaders)

**Problem:** Inconsistent error handling patterns across resource loaders.

**Impact:** **MEDIUM** - Confusing for developers, inconsistent API

**Recommendation:** 
- **Option 1**: All loaders throw exceptions on failure (fail fast)
- **Option 2**: All loaders return null and document that null is expected
- **Option 3**: All loaders use placeholders (fallback behavior)

**Status:** Needs decision on consistent error handling strategy

---

### 5.2 ⚠️ ShaderService Has LRU Cache, Other Loaders Don't

**Comparison:**

| Service | Caching Strategy |
|---------|-----------------|
| `ShaderService` | LRU cache with eviction (MaxCacheSize = 20) |
| `SpriteLoaderService` | Simple dictionary cache (no eviction) |
| `TilesetLoaderService` | Simple dictionary cache (no eviction) |

**Issue:** 
- `ShaderService` has sophisticated LRU caching
- Other loaders use simple dictionary caches
- Inconsistent caching strategies

**Problem:** 
- Shaders are cached with eviction, textures are cached indefinitely
- May cause memory issues with textures if many are loaded

**Impact:** **LOW** - Different resources may need different caching strategies

**Recommendation:** 
- Document why shaders use LRU cache (may be more memory-intensive)
- Consider adding LRU cache to texture loaders if memory becomes an issue

**Status:** May be intentional (shaders vs textures have different memory characteristics)

---

### 5.3 ⚠️ ShaderService Validates ID Format, Other Loaders Don't

**Comparison:**

| Service | ID Validation |
|---------|--------------|
| `ShaderService` | Validates format: `{namespace}:shader:{name}` (all lowercase) |
| `SpriteLoaderService` | No format validation |
| `TilesetLoaderService` | No format validation |

**Issue:** 
- `ShaderService` enforces strict ID format
- Other loaders accept any string ID

**Problem:** 
- Inconsistent validation
- Shader IDs must match format, sprite/tileset IDs don't

**Impact:** **LOW** - May be intentional (shader IDs have specific format requirement)

**Recommendation:** 
- Document why shader IDs require specific format
- Consider adding format validation to other loaders if needed

**Status:** May be intentional (shader IDs have mod format requirement)

---

### 5.4 ✅ Consistent Mod Resource Loading Pattern

**Comparison:**

All three services follow the same pattern:
1. Get definition metadata from `IModManager`
2. Get mod manifest by definition ID
3. Resolve file path: `Path.Combine(modManifest.ModDirectory, definition.FilePath)`
4. Load resource from file system
5. Cache result

**Status:** ✅ **GOOD** - Consistent pattern across all resource loaders

---

## 6. Summary of Required Fixes

### Critical (Must Fix)
1. ❌ **ShaderService.LoadShader()**: Remove null return, throw exception on failure
2. ❌ **ShaderManagerSystem**: Fire `ShaderParameterChangedEvent` when parameters change directly

### Important (Should Fix)
3. ⚠️ **ShaderParameterAnimationSystem**: Extract duplicate `UpdateAnimation()` methods
4. ⚠️ **ShaderManagerSystem**: Extract duplicate `UpdateAllLayersScreenSize()` code
5. ⚠️ **ShaderService**: Add `ValidateShaderIdFormat()` to all public methods

### Nice to Have (Consider Fixing)
6. ⚠️ **ShaderManagerSystem**: Consider splitting into smaller classes (SRP)
7. ⚠️ **Error Handling**: Decide on consistent error handling strategy across all resource loaders
8. ⚠️ **ShaderLoader**: Consider catching specific MonoGame exceptions

---

## 7. Recommendations

### Immediate Actions
1. Fix `ShaderService.LoadShader()` to throw exceptions instead of returning null
2. Add event firing in `ShaderManagerSystem.UpdateShaderParametersForEntity()`
3. Extract duplicate code in `ShaderParameterAnimationSystem`

### Future Considerations
1. Decide on consistent error handling strategy (fail fast vs null returns)
2. Consider splitting `ShaderManagerSystem` into smaller classes
3. Document why shader IDs require specific format (if intentional)

---

## 8. Compliance with .cursorrules

### ✅ Compliant
- ✅ Constructor validation (all constructors validate parameters)
- ✅ XML documentation (all public APIs documented)
- ✅ Namespace matches folder structure
- ✅ One class per file
- ✅ Dependency injection (required dependencies in constructor)

### ❌ Violations
- ❌ **NO FALLBACK CODE**: `ShaderService.LoadShader()` returns null on failure
- ❌ **NO FALLBACK CODE**: `ShaderManagerSystem` continues on shader load failure (if shaders are required)

### ⚠️ Needs Clarification
- ⚠️ **Event Subscriptions**: Events fired but not subscribed (matches codebase pattern)
- ⚠️ **ShaderManagerSystem**: Not a BaseSystem (intentional but inconsistent)

---

**End of Analysis**


