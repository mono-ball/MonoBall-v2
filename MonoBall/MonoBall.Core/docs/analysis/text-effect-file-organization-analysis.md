# Text Effect File Organization Analysis

**Date:** 2024-12-19  
**Scope:** Analysis of current file organization for text effect code and recommendations for improvement.

---

## Current Organization

### Files and Locations

**Enums** (in `Scenes/Components/`):
- `TextEffectType.cs` - Effect types enum (`MonoBall.Core.Scenes.Components`)
- `ColorEffectMode.cs` - Color mode enum (`MonoBall.Core.Scenes.Components`)
- `ShadowEffectMode.cs` - Shadow mode enum (`MonoBall.Core.Scenes.Components`)
- `WobbleOrigin.cs` - Wobble origin enum (`MonoBall.Core.Scenes.Components`)

**Definition** (in `Mods/`):
- `TextEffectDefinition.cs` - Effect definition class (`MonoBall.Core.Mods`)

**Calculator** (in `Rendering/`):
- `ITextEffectCalculator.cs` - Calculator interface (`MonoBall.Core.Rendering`)
- `TextEffectCalculator.cs` - Calculator implementation (`MonoBall.Core.Rendering`)

**Usage** (scattered):
- `Scenes/Systems/MessageBoxSceneSystem.cs` - Manages effect lifecycle
- `UI/Windows/Content/MessageBoxContentRenderer.cs` - Renders effects
- `Scenes/Components/MessageBoxComponent.cs` - Stores effect state
- `Scenes/Components/CharacterRenderData.cs` - Stores per-character effect data

---

## Issues with Current Organization

### 1. **Enums in Wrong Location** 🔴
**Problem:** Text effect enums are in `Scenes/Components/` but they're not scene-specific components. They're domain types used by:
- `TextEffectDefinition` (in `Mods/`)
- `TextEffectCalculator` (in `Rendering/`)
- Various systems and renderers

**Impact:** 
- Namespace doesn't match usage (`Scenes.Components` suggests scene-specific, but they're used across mods and rendering)
- Violates namespace matching folder structure rule
- Makes it unclear these are text effect domain types

### 2. **Scattered Domain Types** 🟡
**Problem:** Text effect domain types are split across three folders:
- Enums in `Scenes/Components/`
- Definition in `Mods/`
- Calculator in `Rendering/`

**Impact:**
- Hard to find all text effect code
- No clear domain boundary
- Inconsistent with other features (Audio, Maps have dedicated folders)

### 3. **Namespace Mismatch** 🟡
**Problem:** `TextEffectDefinition` is in `MonoBall.Core.Mods` but uses enums from `MonoBall.Core.Scenes.Components`.

**Impact:**
- Dependency direction is wrong (Mods depends on Scenes.Components)
- Should be: Mods → TextEffects → (no dependency on Scenes)

---

## Comparison with Other Features

### Audio Feature
```
Audio/
├── AudioDefinition.cs          (mod definition)
├── AudioEngine.cs              (core logic)
├── IAudioEngine.cs             (interface)
└── Core/                       (implementation details)
```

**Pattern:** Top-level feature folder with all related code.

### Maps Feature
```
Maps/
├── MapDefinition.cs            (mod definition)
├── MapLayer.cs                 (domain types)
├── TilesetDefinition.cs        (mod definition)
└── Utilities/                  (helpers)
```

**Pattern:** Top-level feature folder with mod definitions and domain types together.

---

## Recommended Organization

### Option 1: Create `TextEffects/` Top-Level Folder ✅ **RECOMMENDED**

```
TextEffects/
├── TextEffectType.cs           (enum)
├── ColorEffectMode.cs           (enum)
├── ShadowEffectMode.cs          (enum)
├── WobbleOrigin.cs             (enum)
├── TextEffectDefinition.cs     (move from Mods/)
├── ITextEffectCalculator.cs    (move from Rendering/)
└── TextEffectCalculator.cs      (move from Rendering/)
```

**Namespace:** `MonoBall.Core.TextEffects`

**Pros:**
- ✅ All text effect code in one place
- ✅ Clear domain boundary
- ✅ Consistent with Audio/Maps pattern
- ✅ Namespace matches folder structure
- ✅ Easy to find all text effect code
- ✅ No cross-folder dependencies for domain types

**Cons:**
- ⚠️ Requires moving files and updating namespaces
- ⚠️ Breaking change (but project allows breaking changes)

**Migration:**
1. Create `TextEffects/` folder
2. Move enums from `Scenes/Components/` → `TextEffects/`
3. Move `TextEffectDefinition.cs` from `Mods/` → `TextEffects/`
4. Move calculator files from `Rendering/` → `TextEffects/`
5. Update all namespaces and using statements

---

### Option 2: Keep Current Structure, Move Enums to `Mods/`

```
Mods/
├── TextEffectDefinition.cs     (already here)
├── TextEffectType.cs           (move from Scenes/Components/)
├── ColorEffectMode.cs          (move from Scenes/Components/)
├── ShadowEffectMode.cs         (move from Scenes/Components/)
└── WobbleOrigin.cs            (move from Scenes/Components/)

Rendering/
├── ITextEffectCalculator.cs    (keep here)
└── TextEffectCalculator.cs     (keep here)
```

**Namespace:** `MonoBall.Core.Mods` for enums

**Pros:**
- ✅ Minimal changes
- ✅ Enums near definition that uses them

**Cons:**
- ❌ Enums aren't really "mod" types - they're domain types
- ❌ Calculator in Rendering uses Mods namespace (wrong dependency direction)
- ❌ Doesn't match Audio/Maps pattern

---

### Option 3: Keep Current Structure, Move Enums to `Rendering/`

```
Rendering/
├── TextEffectType.cs           (move from Scenes/Components/)
├── ColorEffectMode.cs          (move from Scenes/Components/)
├── ShadowEffectMode.cs         (move from Scenes/Components/)
├── WobbleOrigin.cs            (move from Scenes/Components/)
├── ITextEffectCalculator.cs    (already here)
└── TextEffectCalculator.cs     (already here)

Mods/
└── TextEffectDefinition.cs     (keep here)
```

**Namespace:** `MonoBall.Core.Rendering` for enums

**Pros:**
- ✅ Calculator and enums together
- ✅ Minimal changes

**Cons:**
- ❌ Enums aren't really "rendering" types - they're domain types
- ❌ Mods depends on Rendering (wrong dependency direction)
- ❌ Doesn't match Audio/Maps pattern

---

## Recommendation: Option 1

**Create `TextEffects/` as a top-level feature folder** (like `Audio/` and `Maps/`).

### Rationale:
1. **Consistency:** Matches the pattern used by other features (Audio, Maps)
2. **Domain Clarity:** Text effects are a cohesive feature domain
3. **Dependency Direction:** Correct dependency flow:
   - `Mods/` → `TextEffects/` (mod definitions use text effect types)
   - `Rendering/` → `TextEffects/` (renderers use text effect types)
   - `Scenes/` → `TextEffects/` (scenes use text effect types)
4. **Discoverability:** All text effect code in one place
5. **Namespace Match:** Folder structure matches namespace structure

### File Moves Required:

**From `Scenes/Components/` → `TextEffects/`:**
- `TextEffectType.cs`
- `ColorEffectMode.cs`
- `ShadowEffectMode.cs`
- `WobbleOrigin.cs`

**From `Mods/` → `TextEffects/`:**
- `TextEffectDefinition.cs`

**From `Rendering/` → `TextEffects/`:**
- `ITextEffectCalculator.cs`
- `TextEffectCalculator.cs`

### Namespace Updates:

**Old:** `MonoBall.Core.Scenes.Components`  
**New:** `MonoBall.Core.TextEffects`

**Old:** `MonoBall.Core.Mods` (for TextEffectDefinition)  
**New:** `MonoBall.Core.TextEffects`

**Old:** `MonoBall.Core.Rendering` (for calculator)  
**New:** `MonoBall.Core.TextEffects`

---

## Files That Need Using Statement Updates

After reorganization, these files will need updated using statements:

1. `Mods/ModManager.cs` - Uses `TextEffectDefinition`
2. `Scenes/Systems/MessageBoxSceneSystem.cs` - Uses all text effect types
3. `UI/Windows/Content/MessageBoxContentRenderer.cs` - Uses all text effect types
4. `Scenes/Components/MessageBoxComponent.cs` - Uses text effect types
5. `Scenes/Components/CharacterRenderData.cs` - Uses text effect types
6. Any other files that reference text effect types

---

## Implementation Steps

1. ✅ Create `TextEffects/` folder
2. ✅ Move enum files and update namespaces
3. ✅ Move `TextEffectDefinition.cs` and update namespace
4. ✅ Move calculator files and update namespaces
5. ✅ Update all using statements across codebase
6. ✅ Verify compilation
7. ✅ Update documentation

---

## Summary

**Current State:** Text effect code is scattered across 3 folders with namespace mismatches.

**Recommended:** Create `TextEffects/` top-level folder and consolidate all text effect domain code there.

**Benefits:**
- Consistent with project patterns (Audio, Maps)
- Clear domain boundary
- Correct dependency direction
- Better discoverability
- Namespace matches folder structure

