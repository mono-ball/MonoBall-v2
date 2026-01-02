# Convention-Based Definition Discovery - Type Mismatch Analysis

**Date**: 2025-01-XX  
**Purpose**: Identify all places where services query definition types that may not match convention-based discovery type names

---

## ✅ FIXED ISSUES

### 1. ✅ ConstantsService Type Mismatch - FIXED

**Location**: `ConstantsService.cs` (lines 267, 67)

**Issue**: 
- Service queried: `"ConstantsDefinitions"`
- Convention infers: `"Constants"`
- **Status**: ✅ **FIXED** - Updated to use `"Constants"`

---

## 🔍 POTENTIAL ISSUES FOUND

### 2. ValidateBehaviorDefinitions Query

**Location**: `ModLoader.cs` (line 757)

**Code**:
```csharp
var behaviorDefinitionIds = _registry.GetByType("Behavior").ToList();
```

**Analysis**:
- Querying for type: `"Behavior"`
- However, behaviors were **removed from convention-based discovery** per design document
- No path mapping exists in `KnownPathMappings` for behaviors
- This validation will always find 0 behaviors and return early (harmless)
- **Status**: ⚠️ **HARMLESS** - Code is safe but may be obsolete

**Recommendation**: 
- If behaviors are handled through a different mechanism (not convention-based), this is fine
- If behaviors should be loaded through convention-based discovery, need to add path mapping
- Consider removing this validation if behaviors are no longer used

---

## 🔍 SEARCH RESULTS

### GetByType Usage Found:

1. **ConstantsService.cs** (line 267, 67) - ✅ **FIXED**
   - Changed from `"ConstantsDefinitions"` → `"Constants"`

2. **ModLoader.cs** (line 757) - ⚠️ **ValidateBehaviorDefinitions**
   - Querying: `"Behavior"`
   - No path mapping exists (behaviors removed from design)
   - Harmless (returns early if 0 found)

### No Other GetByType Calls Found

Searched entire `MonoBall.Core` codebase - only these two locations use `GetByType()`.

---

## 📋 TYPE NAME REFERENCE

### Convention-Based Type Names (from KnownPathMappings):

**Assets**:
- `AudioAsset`, `BattleAsset`, `CharacterAsset`, `FieldEffectAsset`, `FontAsset`
- `DoorAnimationAsset`, `ObjectAsset`, `PokemonAsset`, `ShaderAsset`, `SpriteAsset`
- `TilesetAsset`, `InterfaceAsset`, `PopupBackgroundAsset`, `PopupOutlineAsset`
- `TextWindowAsset`, `WeatherAsset`

**Constants**:
- `Constants` ✅

**Entities**:
- `BattleScene`, `Map`, `MapSection`, `Pokemon`, `PopupTheme`, `Region`
- `ColorPalette`, `TextEffect`, `Weather`

**Scripts**:
- `Script`

**Legacy**:
- `Sprite`, `Audio`, `TextWindow`, `TextEffect`, `ColorPalette`, `Font`, `Shader`

---

## ✅ VERIFICATION CHECKLIST

- [x] ConstantsService - ✅ Fixed (`"ConstantsDefinitions"` → `"Constants"`)
- [x] ValidateBehaviorDefinitions - ⚠️ Harmless (behaviors not in convention)
- [x] No other services found querying by type

---

## 🎯 CONCLUSION

**All critical type mismatches have been fixed.**

The only remaining `GetByType()` call is in `ValidateBehaviorDefinitions`, which queries for `"Behavior"`. Since behaviors were removed from convention-based discovery (per design document), this validation will always find 0 behaviors and return early, which is harmless.

If behaviors are supposed to be loaded through convention-based discovery in the future, a path mapping would need to be added to `KnownPathMappings`.
