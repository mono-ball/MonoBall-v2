# Convention-Based Definition Discovery - .cursorrules Violations Analysis

## Overview

This document analyzes the convention-based definition discovery implementation for violations of `.cursorrules` standards, particularly focusing on the **NO BACKWARD COMPATIBILITY** rule.

---

## Critical Violations Found

### ❌ **Violation 1: Legacy/Flat Structure Support in KnownPathMappings**

**Location**: `MonoBall.Core/Mods/TypeInference/KnownPathMappings.cs:50-57`

**Violation**: Lines 50-57 contain "Legacy/flat structure support" mappings that allow old directory structures to work.

**Current Code**:
```csharp
// Legacy/flat structure support
{ "Definitions/Sprites", "Sprite" },
{ "Definitions/Audio", "Audio" },
{ "Definitions/TextWindow", "TextWindow" },
{ "Definitions/TextEffects", "TextEffect" },
{ "Definitions/ColorPalettes", "ColorPalette" },
{ "Definitions/Fonts", "Font" },
{ "Definitions/Shaders", "Shader" },
```

**Rule Violated**: 
- **Rule 1: NO BACKWARD COMPATIBILITY** - "NEVER maintain backward compatibility - refactor APIs freely when improvements are needed"

**Impact**: 
- Allows mods to use old flat structure (`Definitions/Sprites/` instead of `Definitions/Assets/Sprites/`)
- Prevents enforcing the new convention-based structure
- Creates confusion about which structure is correct

**Required Fix**: Remove all legacy/flat structure mappings (lines 50-57).

---

### ❌ **Violation 2: ModDirectory Property Kept for Backward Compatibility**

**Location**: `MonoBall.Core/Mods/ModManifest.cs:98-103`

**Violation**: `ModDirectory` property has comment "Kept for backward compatibility".

**Current Code**:
```csharp
/// <summary>
///     Full path to the mod directory. Set by the loader.
///     Kept for backward compatibility.
/// </summary>
[JsonIgnore]
public string ModDirectory { get; set; } = string.Empty;
```

**Rule Violated**: 
- **Rule 1: NO BACKWARD COMPATIBILITY** - "NEVER maintain backward compatibility"

**Impact**: 
- Property exists only for backward compatibility
- May be used by old code that should be updated

**Required Fix**: 
- Check if `ModDirectory` is still used anywhere
- If used, update call sites to use `ModSource.SourcePath` instead
- Remove `ModDirectory` property entirely

---

## Analysis of Other Rules

### ✅ Rule 2: NO FALLBACK CODE
**Status**: Compliant

- No fallback code found in mod loading
- Missing dependencies throw exceptions (fail fast)
- No default values for required dependencies

### ✅ Rule 3: ECS Systems
**Status**: N/A

- Mod loading is not an ECS system

### ✅ Rule 4: ECS Components
**Status**: N/A

- No components created in mod system

### ✅ Rule 5: Event Subscriptions
**Status**: Compliant

- `DefinitionDiscoveredEvent` is fired when definitions are loaded
- No event subscription leaks found

### ✅ Rule 6: Nullable Types
**Status**: Compliant

- Nullable types used appropriately (`ModManifest?`, `IModSource?`)
- Null checks performed where needed

### ✅ Rule 7: Dependency Injection
**Status**: Compliant

- All dependencies injected via constructor
- `ArgumentNullException` thrown for null dependencies

### ✅ Rule 8: XML Documentation
**Status**: Compliant

- All public APIs documented with XML comments
- Parameters, returns, and exceptions documented

### ✅ Rule 9: Namespace
**Status**: Compliant

- Namespaces match folder structure
- Root namespace: `MonoBall.Core`

### ✅ Rule 10: File Organization
**Status**: Compliant

- One class per file
- File names match class names
- Code order follows convention

---

## Required Fixes

### Fix 1: Remove Legacy Path Mappings

**File**: `MonoBall.Core/Mods/TypeInference/KnownPathMappings.cs`

**Action**: Remove lines 50-57 (legacy/flat structure support).

**Before**:
```csharp
// Scripts (matches all subdirectories: Interactions/, Movement/, Triggers/, etc.)
{ "Definitions/Scripts", "Script" },
// Legacy/flat structure support
{ "Definitions/Sprites", "Sprite" },
{ "Definitions/Audio", "Audio" },
{ "Definitions/TextWindow", "TextWindow" },
{ "Definitions/TextEffects", "TextEffect" },
{ "Definitions/ColorPalettes", "ColorPalette" },
{ "Definitions/Fonts", "Font" },
{ "Definitions/Shaders", "Shader" },
```

**After**:
```csharp
// Scripts (matches all subdirectories: Interactions/, Movement/, Triggers/, etc.)
{ "Definitions/Scripts", "Script" },
```

**Impact**: 
- Mods using old flat structure will fail to load (as intended per project rules)
- Forces mods to use new convention-based structure
- Clear error messages will guide mod authors to fix their structure

---

### Fix 2: Remove ModDirectory Property

**File**: `MonoBall.Core/Mods/ModManifest.cs`

**Action**: 
1. Search for all usages of `ModDirectory` property
2. Update call sites to use `ModSource.SourcePath` instead
3. Remove `ModDirectory` property

**Before**:
```csharp
/// <summary>
///     Full path to the mod directory. Set by the loader.
///     Kept for backward compatibility.
/// </summary>
[JsonIgnore]
public string ModDirectory { get; set; } = string.Empty;
```

**After**: Remove entirely (after updating call sites).

**Impact**: 
- Code using `ModDirectory` will need to be updated
- Forces use of `ModSource` abstraction (better design)

---

## Search for ModDirectory Usages

**Found Usages**:

1. **`ModLoader.cs:302`** - Sets `manifest.ModDirectory = modSource.SourcePath;` with comment "Set ModDirectory for backward compatibility"
2. **`ModManifestLoader.cs:16, 44`** - Parameter `sourcePath` has comment "for backward compatibility" and sets `manifest.ModDirectory = sourcePath ?? string.Empty;`
3. **`ScriptLoaderService.cs:107`** - Passes `mod.ModDirectory` to `LoadPluginScript()` but **parameter is unused** (method uses `modManifest.ModSource` instead)
4. **`ScriptLoaderService.cs:474`** - Uses `Path.Combine(mod.ModDirectory, assemblyPath)` to load assemblies - **NEEDS UPDATE** to use `ModSource`

**Analysis**:
- `LoadPluginScript` parameter `modDirectory` is **unused** - method already uses `ModSource` (line 377, 390)
- Assembly loading at line 474 **still uses ModDirectory** - needs to be updated to use `ModSource.ReadFile()` or extract from archive
- `ModManifestLoader.LoadFromJson()` parameter `sourcePath` is only used for backward compatibility

---

## Summary

**Total Violations**: 2

1. ❌ **Legacy path mappings** in `KnownPathMappings.cs` (lines 50-57)
2. ❌ **ModDirectory property** kept for backward compatibility in `ModManifest.cs` (line 100)

**Priority**: **Critical** - Both violate Rule 1 (NO BACKWARD COMPATIBILITY)

**Action Required**: 
1. Remove legacy path mappings immediately
2. Find and update all `ModDirectory` usages, then remove the property
3. Update any mods using old structure to use new convention

---

## Migration Impact

After removing legacy support:
- Mods using `Definitions/Sprites/` will fail - must move to `Definitions/Assets/Sprites/`
- Mods using `Definitions/Audio/` will fail - must move to `Definitions/Assets/Audio/`
- Mods using `Definitions/TextWindow/` will fail - must move to `Definitions/Assets/UI/TextWindows/`
- Mods using `Definitions/TextEffects/` will fail - must move to `Definitions/Entities/Text/TextEffects/`
- Mods using `Definitions/ColorPalettes/` will fail - must move to `Definitions/Entities/Text/ColorPalettes/`
- Mods using `Definitions/Fonts/` will fail - must move to `Definitions/Assets/Fonts/`
- Mods using `Definitions/Shaders/` will fail - must move to `Definitions/Assets/Shaders/`

This is **intended behavior** per project rules - mods must be updated to use the new convention.
