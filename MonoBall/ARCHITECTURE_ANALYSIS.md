# Architecture Analysis: Recent Changes

## Overview
This document analyzes recent changes for architectural problems, SOLID/DRY violations, potential bugs, and hacky patterns.

## Issues Identified

### 🔴 CRITICAL: Background Color Logic is Hacky (MonoBallGame.Draw)

**Location:** `MonoBallGame.cs:338-350`

**Problem:**
```csharp
if (systemManager != null)
{
    GraphicsDevice.Clear(Color.Black);  // Game scene
}
else
{
    GraphicsDevice.Clear(new Color(234, 234, 233));  // Loading screen
}
```

**Issues:**
1. **Violates Single Responsibility Principle**: `MonoBallGame` shouldn't know about scene types or rendering details
2. **Tight Coupling**: Couples game class to internal SystemManager state
3. **Fragile**: Assumes `systemManager == null` always means loading screen, but what if SystemManager fails to initialize?
4. **Not Scalable**: What if we add more scene types? We'd need more conditionals here
5. **Wrong Abstraction Level**: Background color should be determined by the scene system, not the game class

**Better Approach:**
- Let `SceneRendererSystem` or individual scene renderers determine background color
- Or create a `BackgroundColorComponent` that scenes can set
- Or query the active scene and ask it for its background color

---

### 🟡 MODERATE: FontService Creation Duplication (DRY Violation)

**Locations:**
- `MonoBallGame.LoadCoreModSynchronously()` (line 407-412)
- `GameServices.LoadMods()` (line 211-227)

**Problem:**
FontService creation logic is duplicated in two places with slightly different checks:
1. `LoadCoreModSynchronously()` - Always creates and registers
2. `LoadMods()` - Checks if exists, creates if not

**Issues:**
1. **DRY Violation**: Same logic in two places
2. **Maintenance Risk**: Changes to FontService creation must be made in two places
3. **Inconsistency Risk**: Two code paths could diverge over time
4. **Unclear Ownership**: Which method is responsible for FontService lifecycle?

**Better Approach:**
- Extract FontService creation to a helper method: `CreateAndRegisterFontService()`
- Or make `GameServices` responsible for ALL service creation (including FontService)
- Or create a `ServiceFactory` class

---

### 🟡 MODERATE: Defensive Font Cache Check Performance

**Location:** `FontService.cs:50-82`

**Problem:**
Every font access validates the cached FontSystem by calling `GetFont(12)`:
```csharp
var testFont = cachedFont.GetFont(12);
```

**Issues:**
1. **Performance Concern**: Validation happens on EVERY cache hit (hot path)
2. **Unnecessary**: FontSystem shouldn't lose fonts unless there's a bug in FontStashSharp
3. **Exception Handling Overhead**: Try-catch on every access adds overhead
4. **Defensive Programming Gone Too Far**: We're guarding against a problem that shouldn't exist

**Better Approach:**
- Remove defensive check from hot path
- Add validation only when FontSystem is first created/cached
- Or make it optional/configurable (only validate in debug builds)
- If FontSystem really can lose fonts, that's a bug in FontStashSharp that should be fixed upstream

---

### 🟡 MODERATE: FontService Disposal Issue

**Location:** `LoadingSceneRendererSystem.cs:364-372`

**Problem:**
```csharp
public new void Dispose()
{
    _fontSystem?.Dispose();
    _fontSystem = null;
}
```

**Issues:**
1. **Shared Resource Disposal**: `FontSystem` is obtained from `FontService`, which caches it
2. **Double Disposal Risk**: If multiple systems dispose the same FontSystem, it could cause issues
3. **Cache Corruption**: Disposing a cached FontSystem could corrupt FontService's cache
4. **Unclear Ownership**: Who owns the FontSystem - the service or the system?

**Better Approach:**
- Systems should NOT dispose FontSystem instances from FontService
- FontService should own and manage FontSystem lifecycle
- Systems should only dispose resources they create themselves
- If FontSystem needs disposal, FontService should handle it in its own Dispose()

---

### 🟢 MINOR: Inconsistent Service Registration Pattern

**Location:** `GameServices.cs` vs `MonoBallGame.cs`

**Problem:**
- `MonoBallGame.LoadCoreModSynchronously()` directly calls `Services.AddService()`
- `GameServices.LoadMods()` checks if service exists before calling `AddService()`

**Issues:**
1. **Inconsistent Pattern**: Two different approaches to service registration
2. **MonoGame AddService Behavior Unknown**: Does it throw if service exists? Replace? We're checking but not sure

**Better Approach:**
- Research MonoGame's `AddService` behavior (does it throw or replace?)
- Create a helper method: `RegisterServiceIfNotExists<T>()` or `GetOrCreateService<T>()`
- Standardize on one pattern across the codebase

---

### 🟢 MINOR: Magic Numbers in Loading Screen

**Location:** `LoadingSceneRendererSystem.cs`

**Problem:**
Hard-coded color values scattered throughout:
```csharp
private static readonly Color BackgroundColor = new(234, 234, 233);
private static readonly Color ProgressBarFillColor = new(235, 72, 60);
```

**Issues:**
1. **Magic Numbers**: Hard to understand what colors represent
2. **Not Reusable**: Can't easily change theme
3. **No Documentation**: Colors don't have semantic names

**Better Approach:**
- Create a `LoadingScreenTheme` class with named color properties
- Or use a configuration/theme system
- Or at least add XML comments explaining color choices

---

## Recommendations

### Priority 1: Fix Background Color Logic
1. Create `IBackgroundColorProvider` interface
2. Have scenes implement it or add `BackgroundColorComponent`
3. Query active scene for background color in `SceneRendererSystem`
4. Remove background color logic from `MonoBallGame.Draw()`

### Priority 2: Consolidate FontService Creation
1. Extract `CreateAndRegisterFontService()` helper method
2. Call from single location (probably `GameServices`)
3. Remove duplication from `MonoBallGame`

### Priority 3: Optimize Font Cache Validation
1. Remove defensive check from hot path
2. Only validate when FontSystem is first created
3. Add assertion/validation in debug builds only

### Priority 4: Fix FontSystem Disposal
1. Remove `FontSystem.Dispose()` from `LoadingSceneRendererSystem`
2. Systems should not dispose shared resources from services
3. Add `IDisposable` to `FontService` if needed for cleanup

### Priority 5: Standardize Service Registration
1. Research MonoGame `AddService` behavior
2. Create helper method for safe service registration
3. Use consistently across codebase

---

## SOLID Principles Violations

### Single Responsibility Principle (SRP)
- ❌ `MonoBallGame.Draw()` determines background color based on scene type
- ❌ `GameServices.LoadMods()` handles both ModManager AND FontService creation

### Open/Closed Principle (OCP)
- ⚠️ Background color logic requires modification to add new scene types

### Dependency Inversion Principle (DIP)
- ✅ Services are injected through Game.Services (good)
- ⚠️ Direct coupling to SystemManager state for background color

---

## Potential Bugs

1. **Race Condition**: If `systemManager` is set to null after initialization, background color would switch back to loading screen
2. **FontSystem Disposal**: Disposing cached FontSystem could corrupt FontService cache
3. **Service Replacement**: If MonoGame's `AddService` replaces services, we could lose FontService cache (though we check for this)
4. **Performance**: Font cache validation on every access could impact performance

---

## Code Smells

1. **Feature Envy**: `MonoBallGame.Draw()` knows too much about scene rendering
2. **Duplicated Code**: FontService creation in two places
3. **Defensive Programming**: Over-defensive font cache validation
4. **Magic Numbers**: Hard-coded color values
5. **Tight Coupling**: Background color logic couples game class to scene system
