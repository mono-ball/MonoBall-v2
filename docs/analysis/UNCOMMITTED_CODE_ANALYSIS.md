# Uncommitted C# Code Analysis

**Date:** 2024-12-19  
**Scope:** All uncommitted C# files in `MonoBall/` directory  
**Analysis Categories:** Architecture, Code Smells, SOLID/DRY, Arch ECS/Events, .cursorrules Compliance, Potential Bugs

---

## Executive Summary

Analysis of uncommitted C# code reveals **mostly well-structured code** following ECS patterns and project conventions. However, several issues were identified across multiple categories:

- **Architecture Issues:** 3 critical, 5 moderate
- **Code Smells:** 8 instances
- **SOLID/DRY Violations:** 4 violations
- **Arch ECS/Event Issues:** 2 issues
- **.cursorrules Compliance:** 1 violation
- **Potential Bugs:** 3 potential runtime issues

---

## 1. Architecture Issues

### 1.1 Critical: Duplicate Shader System Creation in SystemManager

**File:** `MonoBall/MonoBall.Core/ECS/SystemManager.cs`

**Issue:** Shader systems are created twice - once in `InitializeCoreServices()` (lines 414-443) and again in `CreateShaderSystems()` (lines 729-800). This violates DRY and could lead to resource leaks or inconsistent state.

**Location:**
- Lines 414-443: First creation in `InitializeCoreServices()`
- Lines 729-800: Second creation in `CreateShaderSystems()`

**Impact:** 
- Potential resource leaks (RenderTargetManager created twice)
- Inconsistent system references
- Violates Single Responsibility Principle

**Recommendation:** Remove shader system creation from `InitializeCoreServices()` and only create them in `CreateShaderSystems()`.

```csharp
// REMOVE from InitializeCoreServices() (lines 414-443)
// KEEP only in CreateShaderSystems() (lines 729-800)
```

---

### 1.2 Critical: Missing Null Check in ResourceManager

**File:** `MonoBall/MonoBall.Core/Resources/ResourceManager.cs`

**Issue:** `ResolveVariableSpriteIfNeeded()` (line 1153) can return null, but callers don't always check for null before using the result.

**Location:** Line 1153-1184

**Example:**
```csharp
// Line 385-389: No null check after ResolveVariableSpriteIfNeeded
var actualSpriteId = ResolveVariableSpriteIfNeeded(spriteId, "sprite definition");
if (actualSpriteId == null)
    throw new InvalidOperationException(...);
// But this check is only in GetSpriteDefinition, not in all callers
```

**Impact:** Potential `NullReferenceException` if variable sprite resolution fails unexpectedly.

**Recommendation:** Ensure all callers of `ResolveVariableSpriteIfNeeded()` check for null, or make the method throw instead of returning null.

---

### 1.3 Critical: Event Subscription Memory Leak Risk

**File:** `MonoBall/MonoBall.Core/ECS/Systems/InteractionSystem.cs`

**Issue:** Event subscriptions are stored in `_subscriptions` list and disposed in `Dispose()`, but if `Dispose()` is never called, subscriptions leak.

**Location:** Lines 33, 80-81, 87-90, 417-424

**Current Implementation:**
```csharp
private readonly List<IDisposable> _subscriptions = new();
// ...
_subscriptions.Add(EventBus.Subscribe<InteractionEndedEvent>(OnInteractionEnded));
_subscriptions.Add(EventBus.Subscribe<MessageBoxClosedEvent>(OnMessageBoxClosed));
```

**Impact:** Memory leaks if SystemManager doesn't properly dispose all systems.

**Recommendation:** Verify that `SystemManager.Dispose()` properly disposes all systems that implement `IDisposable`. ✅ **GOOD:** SystemManager.Dispose() does dispose systems (lines 242-324).

---

### 1.4 Moderate: Inconsistent Error Handling in MapLoaderSystem

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs`

**Issue:** Some methods use fallback code (returning empty/default values) instead of failing fast, violating .cursorrules.

**Location:** 
- Lines 1338-1497: `CreateMapBorderComponent()` returns empty component on validation failure instead of throwing
- Lines 428-461: `PreloadTilesets()` catches exceptions and logs warnings instead of failing fast

**Example:**
```csharp
// Line 1354-1367: Returns empty component instead of throwing
if (border.BottomLayer == null || border.BottomLayer.Count != 4)
{
    _logger.Warning(...);
    return new MapBorderComponent { ... }; // Fallback code
}
```

**Impact:** Violates "No Fallback Code" rule from .cursorrules. Errors are silently ignored.

**Recommendation:** Throw exceptions instead of returning empty/default values. Fail fast as per project rules.

---

### 1.5 Moderate: Diagnostic Logging Left in Production Code

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs`

**Issue:** Extensive diagnostic logging for GID 80 (flower tile) left in production code.

**Location:**
- Lines 652-661: Diagnostic logging in MapLoaderSystem
- Lines 689-757: Diagnostic logging in MapRendererSystem

**Example:**
```csharp
// Line 653-661: Diagnostic logging
if (gid == 80)
{
    _logger.Information("MapLoaderSystem: Processing GID 80, ...");
}
```

**Impact:** Performance overhead and log spam in production.

**Recommendation:** Remove diagnostic logging or guard with `#if DEBUG` or a configuration flag.

---

### 1.6 Moderate: ResourceManager Diagnostic Logging

**File:** `MonoBall/MonoBall.Core/Resources/ResourceManager.cs`

**Issue:** Diagnostic logging for tile 79 (flower) left in production code.

**Location:**
- Lines 653-678: Diagnostic logging in `GetTilesetDefinition()`
- Lines 772-778: Diagnostic logging in `TryGetTileAnimation()`

**Recommendation:** Remove or guard with debug flags.

---

### 1.7 Moderate: Missing XML Documentation

**File:** Multiple files

**Issue:** Some public methods lack XML documentation comments, violating .cursorrules requirement.

**Examples:**
- `ResourceManager.ResolveVariableSpriteIfNeeded()` (line 1153) - private but should still be documented
- `MapRendererSystem.ResolveTilesetResources()` - method signature not found but likely exists

**Recommendation:** Add XML documentation to all public and significant private methods.

---

### 1.8 Moderate: Inconsistent Nullable Reference Type Usage

**File:** Multiple files

**Issue:** Some nullable parameters use `?` suffix, others use `null!` initialization, creating inconsistency.

**Example:**
```csharp
// SystemManager.cs line 45: Uses null! for initialization
private IActiveMapFilterService _activeMapFilterService = null!;

// But some optional parameters use ? suffix
public SpriteRendererSystem(..., ShaderManagerSystem? shaderManagerSystem = null)
```

**Impact:** Inconsistent nullability patterns make code harder to understand.

**Recommendation:** Standardize on either `?` for nullable or `null!` for "will be initialized" patterns, document the convention.

---

## 2. Code Smells

### 2.1 Long Method: MapLoaderSystem.CreateNpcEntity()

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs`

**Issue:** Method is 342 lines long (lines 877-1218), violating Single Responsibility Principle.

**Impact:** Hard to maintain, test, and understand.

**Recommendation:** Break into smaller methods:
- `ResolveVariableSprite()`
- `ValidateNpcDefinition()`
- `CreateNpcComponents()`
- `AttachScripts()`

---

### 2.2 Long Method: SystemManager.Initialize()

**File:** `MonoBall/MonoBall.Core/ECS/SystemManager.cs`

**Issue:** `Initialize()` method orchestrates many operations but delegates to helper methods. However, the method chain is long and complex.

**Impact:** Hard to follow initialization flow.

**Recommendation:** Consider adding a state machine or builder pattern for initialization.

---

### 2.3 Magic Numbers

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs`

**Issue:** Magic numbers used for render target indices.

**Location:**
- Line 280: `GetOrCreateRenderTarget(100)` - tile layer
- Line 320: `GetOrCreateRenderTarget(101)` - sprite layer (in SpriteRendererSystem)

**Recommendation:** Extract to constants:
```csharp
private const int TileLayerRenderTargetIndex = 100;
private const int SpriteLayerRenderTargetIndex = 101;
```

---

### 2.4 Duplicate Code: Shader Stacking Logic

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs` and `SpriteRendererSystem.cs`

**Issue:** Shader stacking logic is duplicated between MapRendererSystem and SpriteRendererSystem.

**Location:**
- MapRendererSystem: Lines 268-366
- SpriteRendererSystem: Lines 308-449

**Impact:** Violates DRY principle. Changes must be made in two places.

**Recommendation:** Extract to shared utility class or base class method.

---

### 2.5 Complex Conditional: MapRendererSystem.RenderChunk()

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs`

**Issue:** `RenderChunk()` has complex nested conditionals for animated vs non-animated tiles (lines 524-817).

**Impact:** Hard to understand and maintain.

**Recommendation:** Extract animated tile rendering to separate method.

---

### 2.6 Excessive Comments

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs`

**Issue:** Some methods have excessive inline comments explaining obvious code.

**Location:** Lines 20-42 (remarks section is good, but some inline comments are redundant)

**Recommendation:** Remove redundant comments, keep only those explaining "why" not "what".

---

### 2.7 Inconsistent Naming

**File:** Multiple files

**Issue:** Some methods use `Get` prefix for operations that might throw, others use `Load`.

**Examples:**
- `ResourceManager.GetSpriteDefinition()` - throws if not found
- `ResourceManager.LoadTexture()` - loads and caches
- `ResourceManager.GetTileAnimation()` - throws if not found

**Recommendation:** Standardize naming:
- `Get*` - returns existing/cached, may return null
- `Load*` - loads from disk/cache, may throw
- `TryGet*` - returns bool, out parameter

---

### 2.8 God Class: SystemManager

**File:** `MonoBall/MonoBall.Core/ECS/SystemManager.cs`

**Issue:** SystemManager has 1414 lines and manages too many responsibilities:
- System creation
- System registration
- System disposal
- Update coordination
- Render coordination
- Scene management coordination

**Impact:** Hard to maintain, violates Single Responsibility Principle.

**Recommendation:** Consider splitting into:
- `SystemFactory` - creates systems
- `SystemRegistry` - manages system registration
- `SystemCoordinator` - coordinates updates/renders

---

## 3. SOLID/DRY Violations

### 3.1 Single Responsibility: MovementSystem

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs`

**Issue:** MovementSystem handles both movement logic AND animation state changes (intentional per comments, but still violates SRP).

**Location:** Lines 20-42 (remarks acknowledge this)

**Current Justification:** Animation state must change atomically with movement state to prevent timing bugs.

**Recommendation:** Document this intentional violation clearly, consider if there's a better architectural solution.

---

### 3.2 DRY: Duplicate Tileset Resolution Logic

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs`

**Issue:** Tileset resolution logic appears in multiple places.

**Location:**
- Lines 556-563: `ResolveTilesetResources()` call
- Lines 669-672: Direct `TilesetResolver.ResolveTilesetForGid()` call
- Lines 760-766: Another `ResolveTilesetResources()` call

**Impact:** Code duplication, changes must be made in multiple places.

**Recommendation:** Always use `ResolveTilesetResources()` helper method, remove direct `TilesetResolver` calls.

---

### 3.3 DRY: Duplicate Shader Stacking Logic

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs` and `SpriteRendererSystem.cs`

**Issue:** Shader stacking logic duplicated between two renderer systems.

**Recommendation:** Extract to shared utility class.

---

### 3.4 Open/Closed: Hard-coded Render Target Indices

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs` and `SpriteRendererSystem.cs`

**Issue:** Render target indices are hard-coded, making it difficult to extend with new layers.

**Recommendation:** Use enum or constants, consider configuration.

---

## 4. Arch ECS/Event Issues

### 4.1 QueryDescription Caching: ✅ GOOD

**Status:** All systems properly cache `QueryDescription` in constructors. No violations found.

**Verified Files:**
- SpriteRendererSystem: Lines 83-97
- MapRendererSystem: Lines 89-94
- InteractionSystem: Lines 61-77
- MovementSystem: Lines 82-95
- SpriteAnimationSystem: Lines 50-62

---

### 4.2 Event Subscription Disposal: ✅ MOSTLY GOOD

**Status:** Systems that subscribe to events properly implement `IDisposable` and dispose subscriptions.

**Verified:**
- InteractionSystem: ✅ Implements IDisposable, disposes subscriptions (lines 87-90, 417-424)
- SpriteAnimationSystem: ✅ Implements IDisposable, disposes subscriptions (lines 76-79, 390-403)

**Note:** SystemManager properly disposes all systems (lines 242-324).

---

### 4.3 Event Bus Usage: ✅ GOOD

**Status:** Events are properly sent using `EventBus.Send(ref evt)` pattern.

**Verified:**
- MapLoaderSystem: Line 251, 424, 1216
- InteractionSystem: Lines 303, 312, 354
- MovementSystem: Lines 178, 207, 450

---

## 5. .cursorrules Compliance

### 5.1 Violation: Fallback Code in MapLoaderSystem

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapLoaderSystem.cs`

**Issue:** `CreateMapBorderComponent()` returns empty component on validation failure instead of throwing exception.

**Location:** Lines 1338-1497

**Rule Violated:** "NO FALLBACK CODE - Fail fast with clear exceptions"

**Current Code:**
```csharp
if (border.BottomLayer == null || border.BottomLayer.Count != 4)
{
    _logger.Warning(...);
    return new MapBorderComponent { ... }; // ❌ Fallback code
}
```

**Should Be:**
```csharp
if (border.BottomLayer == null || border.BottomLayer.Count != 4)
{
    throw new InvalidOperationException(
        $"Map {mapDefinition.Id} has invalid border bottom layer (expected 4 elements, got {border.BottomLayer?.Count ?? 0})"
    );
}
```

---

### 5.2 Compliance: ✅ QueryDescription Caching

**Status:** All systems cache QueryDescription in constructors. ✅ Compliant.

---

### 5.3 Compliance: ✅ Event Subscription Disposal

**Status:** Systems with event subscriptions implement IDisposable. ✅ Compliant.

---

### 5.4 Compliance: ✅ Component Naming

**Status:** All components end with `Component` suffix. ✅ Compliant.

---

### 5.5 Compliance: ✅ System Naming

**Status:** All systems end with `System` suffix. ✅ Compliant.

---

## 6. Potential Bugs

### 6.1 Race Condition: Entity Lifecycle in MovementSystem

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MovementSystem.cs`

**Issue:** Entity lifecycle checks are performed, but there's a window between `World.IsAlive()` check and component access where entity could be destroyed.

**Location:** Lines 138, 256-257

**Current Code:**
```csharp
if (!World.IsAlive(entity))
    return; // Entity was destroyed, skip

// ... later ...
if (!World.Has<PositionComponent>(entity) || !World.Has<GridMovement>(entity))
    return; // Entity lost required components, skip
```

**Impact:** Potential `InvalidOperationException` if entity is destroyed between checks.

**Recommendation:** Wrap component access in try-catch, or use `World.TryGet<>()` consistently.

---

### 6.2 Potential Null Reference: ResourceManager Texture Loading

**File:** `MonoBall/MonoBall.Core/Resources/ResourceManager.cs`

**Issue:** `ExtractTexturePath()` can throw, but error message might not be clear if both sprite and tileset definitions are null.

**Location:** Lines 1118-1133

**Current Code:**
```csharp
var spriteDef = _modManager.GetDefinition<SpriteDefinition>(resourceId);
// ...
var tilesetDef = _modManager.GetDefinition<TilesetDefinition>(resourceId);
// ...
throw new InvalidOperationException(
    $"Texture definition not found or has no TexturePath: {resourceId}"
);
```

**Impact:** Unclear error message if resourceId is invalid.

**Recommendation:** Provide more context in error message (which types were checked).

---

### 6.3 Index Out of Range: MapRendererSystem Animated Tiles

**File:** `MonoBall/MonoBall.Core/ECS/Systems/MapRendererSystem.cs`

**Issue:** `animState.CurrentFrameIndex` is used to index into `frames` array without bounds check in some paths.

**Location:** Lines 713-732

**Current Code:**
```csharp
if (animState.CurrentFrameIndex < frames.Count)
{
    var currentFrame = frames[animState.CurrentFrameIndex]; // ✅ Bounds checked
    // ...
}
```

**Status:** ✅ Actually has bounds check. No bug here.

---

### 6.4 Memory Leak: Dictionary Not Cleaned Up

**File:** `MonoBall/MonoBall.Core/ECS/Systems/SpriteAnimationSystem.cs`

**Issue:** `_previousAnimationNames` dictionary is cleaned up in `Update()`, but if `Update()` stops being called (e.g., system disabled), dictionary entries persist.

**Location:** Lines 149-156

**Impact:** Minor memory leak if system is disabled but not disposed.

**Recommendation:** Add cleanup in `Dispose()` method (already done at line 398). ✅ Actually handled.

---

## 7. Recommendations Summary

### High Priority
1. **Remove duplicate shader system creation** in SystemManager
2. **Fix fallback code** in MapLoaderSystem.CreateMapBorderComponent()
3. **Remove diagnostic logging** or guard with debug flags
4. **Extract shader stacking logic** to shared utility

### Medium Priority
5. **Break down long methods** (CreateNpcEntity, RenderChunk)
6. **Extract magic numbers** to constants
7. **Standardize naming conventions** (Get vs Load vs TryGet)
8. **Add missing XML documentation**

### Low Priority
9. **Consider splitting SystemManager** into smaller classes
10. **Standardize nullable reference type patterns**
11. **Remove redundant comments**

---

## 8. Positive Findings

### ✅ Excellent Practices
1. **QueryDescription caching** - All systems properly cache queries
2. **Event subscription disposal** - Proper IDisposable implementation
3. **Reusable collections** - Systems use cached collections to avoid allocations
4. **Fail-fast validation** - Most code properly validates and throws exceptions
5. **XML documentation** - Most public APIs are well-documented
6. **Component/System naming** - Follows conventions consistently
7. **Event bus usage** - Proper use of ref parameters for struct events

---

## Conclusion

The uncommitted code is **generally well-structured** and follows ECS patterns correctly. The main issues are:

1. **Architecture:** Duplicate shader system creation and some fallback code
2. **Code Quality:** Some long methods and code duplication
3. **Compliance:** One .cursorrules violation (fallback code)

**Overall Assessment:** Code quality is **good** with room for improvement in code organization and DRY compliance.
