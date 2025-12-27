# Shader Mod System Implementation Plan - Analysis

## Overview

This document analyzes the implementation plan for architecture problems, .cursorrules compliance, missing steps, and integration issues.

## Critical Issues Found

### 1. ❌ Contradictory Phase 4.3 Requirements

**Location**: Phase 4.3 (lines 209-228)

**Problem**: The plan states:
- "Remove `ContentManager` dependency from constructor"
- "Update `GetShader()` to call `LoadModShader()` instead"
- "Keep methods but redirect to `LoadModShader()` with clear error messages"

**Issue**: If we remove `ContentManager` dependency, `LoadShader()` cannot work. The plan contradicts itself.

**Fix**: Clarify the approach:
- **Option A**: Remove ContentManager entirely, remove `LoadShader()`, update `GetShader()` to use `LoadModShader()` only
- **Option B**: Keep ContentManager temporarily, make `LoadShader()` detect ID format and route accordingly, then remove in later phase

**Recommendation**: Use Option A - remove ContentManager completely since we're migrating all shaders to mods.

### 2. ❌ Missing IShaderService Interface Update

**Location**: Phase 1.3 (lines 64-87)

**Problem**: Plan adds `LoadModShader()` method to `ShaderService` but doesn't update `IShaderService` interface.

**Issue**: Code using `IShaderService` won't have access to `LoadModShader()` method.

**Fix**: Add `LoadModShader(string shaderId)` method to `IShaderService` interface.

### 3. ❌ GetShader() Caching Logic Unclear

**Location**: Phase 4.3 (line 222)

**Problem**: Plan says "Update `GetShader()` to call `LoadModShader()` instead" but `GetShader()` has LRU caching logic.

**Issues**:
- Should mod shaders be cached? (Yes, for performance)
- Should cache be shared between ContentManager and mod shaders? (N/A if removing ContentManager)
- Cache key format: mod shader IDs use `:` separator, ContentManager uses prefixes

**Fix**: 
- Update `GetShader()` to detect shader ID format
- If mod shader format (`{namespace}:shader:{name}`), call `LoadModShader()` and cache
- If ContentManager format, throw `NotSupportedException` (or remove entirely)
- Cache should work for both ID formats

### 4. ❌ Phase Order Issue

**Location**: Phase 3 vs Phase 4

**Problem**: Phase 3 creates test mod, Phase 4 removes ContentManager. If Phase 4 runs before Phase 3 is verified, we break the build.

**Issue**: Should verify test mod works BEFORE removing ContentManager code.

**Fix**: Reorder phases:
- Phase 3: Create test mod
- **New Phase 3.5**: Test that mod shaders load correctly (before removing ContentManager)
- Phase 4: Remove ContentManager code

### 5. ❌ Missing Shader Usage Discovery

**Location**: Migration Notes (line 316-318)

**Problem**: Plan mentions "All shader references in rendering systems need ID updates" but doesn't specify:
- Where are these references?
- How to find them?
- What the migration strategy is?

**Issue**: Without finding all usages, we can't complete the migration.

**Fix**: Add step to Phase 4:
- Search codebase for `GetShader()`, `LoadShader()`, `shaderService` usages
- Document all locations
- Create migration mapping: `TileLayerColorGrading` → `base:shader:ColorGrading`
- Update all call sites

### 6. ❌ MSBuild Implementation Incomplete

**Location**: Phase 2.1 (lines 101-129)

**Problem**: Plan mentions "PowerShell script approach" but doesn't specify:
- Where to put the PowerShell script?
- How to call it from MSBuild?
- Error handling strategy?
- How to ensure MGFXC tool is available?

**Issues**:
- MSBuild needs explicit script path
- Cross-platform concerns (PowerShell on Windows, pwsh on Linux/Mac)
- Tool availability check needed

**Fix**: Provide detailed implementation:
- Create `build/CompileModShaders.ps1` script
- Use MSBuild `Exec` task with `Command="pwsh -File ..."`
- Add tool restore step or fail with clear error
- Handle JSON parsing in PowerShell (or use C# inline task)

### 7. ❌ ModValidator Integration Unclear

**Location**: Phase 5.1 (lines 232-247)

**Problem**: Plan says "Add method: `ValidateShaderDefinition()`" but doesn't specify:
- Where to call it from?
- How does it integrate with existing validation flow?

**Issue**: `ModValidator` has a specific validation pattern - need to follow it.

**Fix**: Review `ModValidator` structure:
- Check how other definition types are validated
- Integrate shader validation into existing validation flow
- Call from appropriate validation method

### 8. ❌ Missing DefinitionType Consistency Check

**Location**: Phase 1.3 (line 79)

**Problem**: Plan uses `DefinitionType = "Shaders"` but doesn't verify this matches what `ModLoader` will set.

**Issue**: Need to ensure `ModLoader` sets `DefinitionType` correctly based on `contentFolders` key.

**Fix**: Verify that when `contentFolders` has `"Shaders": "Definitions/Shaders"`, `ModLoader` sets `definitionType = "Shaders"` (the key name, not folder path).

### 9. ❌ ShaderLoader Null Check Missing

**Location**: Phase 1.3 (line 76)

**Problem**: Plan says "Validates `_modManager` and `_modShaderLoader` are not null" but `_modShaderLoader` is nullable (`ShaderLoader?`).

**Issue**: If `_modShaderLoader` is null, we should throw, but the check needs to be explicit.

**Fix**: Add explicit null check:
```csharp
if (_modShaderLoader == null)
{
    throw new InvalidOperationException("ShaderLoader is not initialized.");
}
```

### 10. ❌ Missing mod.manifest Update Details

**Location**: Phase 3.3 (lines 188-192)

**Problem**: Plan says "Add `base:test-shaders` to mod load order" but `mod.manifest` currently has:
```json
{
  "modOrder": ["base:monoball-core", "pokemon:emerald"]
}
```

**Issue**: 
- Mod ID mismatch: plan uses `base:test-shaders` but core mod is `base:monoball-core`
- Need to specify exact position (after core, before pokemon?)

**Fix**: Specify exact update:
```json
{
  "modOrder": [
    "base:monoball-core",
    "base:test-shaders",
    "pokemon:emerald"
  ]
}
```

### 11. ❌ Shader Parameter Extraction Not Specified

**Location**: Phase 3.2 (line 162)

**Problem**: Plan says "Extract from `.fx` file (Brightness, Contrast, Saturation, ColorTint)" but doesn't specify:
- How to parse `.fx` files?
- What format for parameters in JSON?
- Manual extraction or automated?

**Issue**: This is a manual, error-prone step.

**Fix**: 
- Option A: Manual extraction with clear format specification
- Option B: Create helper script to parse `.fx` and generate parameter definitions
- Document parameter JSON format clearly

### 12. ❌ Missing Error Handling in ShaderLoader

**Location**: Phase 1.2 (line 60)

**Problem**: Plan says "Reads bytecode and creates `Effect` instance" but doesn't handle:
- Invalid bytecode (Effect constructor can throw)
- File read errors
- GraphicsDevice issues

**Fix**: Add try-catch around `new Effect()` and wrap in `InvalidOperationException` with context.

### 13. ❌ GameServices ModManager Access

**Location**: Phase 1.4 (line 95)

**Problem**: Plan says "Pass `ModManager` to `ShaderService` constructor" but `GameServices` needs access to `ModManager`.

**Issue**: Need to verify `ModManager` is available when `GameServices` initializes shader service.

**Fix**: Check `GameServices` initialization order - ensure `ModManager` is initialized before shader service.

### 14. ❌ Cache Key Collision Risk

**Location**: Phase 4.3 (line 224)

**Problem**: If we keep both `GetShader()` (ContentManager) and `LoadModShader()` (mods), cache keys could collide.

**Issue**: Mod shader ID `base:shader:ColorGrading` vs ContentManager ID `TileLayerColorGrading` - no collision, but if we update `GetShader()` to use mod shaders, need to ensure cache works correctly.

**Fix**: Cache should work fine since IDs are different formats, but document this clearly.

## Architecture Issues

### 1. ShaderService Constructor Breaking Change

**Current**: `ShaderService(ContentManager content, GraphicsDevice graphicsDevice, ILogger logger)`

**Proposed**: Add optional `IModManager?` parameter

**Issue**: This changes the constructor signature, breaking existing code.

**Fix**: 
- Option A: Make `IModManager` required (breaking change, update all call sites)
- Option B: Add overload: `ShaderService(ContentManager, GraphicsDevice, ILogger, IModManager?)`
- Option C: Remove `ContentManager` parameter entirely (breaking change)

**Recommendation**: Since we're removing ContentManager anyway, use Option C - make breaking change in one go.

### 2. Missing Shader ID Format Validation

**Location**: Phase 1.3 `LoadModShader()`

**Problem**: No validation that shader ID matches expected format `{namespace}:shader:{name}`.

**Issue**: Could accept invalid IDs and fail later with unclear errors.

**Fix**: Add validation at start of `LoadModShader()`:
```csharp
if (!shaderId.Contains(":shader:"))
{
    throw new ArgumentException(
        $"Shader ID '{shaderId}' does not match mod shader format. " +
        "Expected format: {{namespace}}:shader:{{name}}",
        nameof(shaderId)
    );
}
```

## Missing Steps

### 1. Find All Shader Usages

**Add to Phase 4**:
- Search codebase for `GetShader()`, `LoadShader()`, `IShaderService` usages
- Document all call sites
- Create migration checklist

### 2. Test Mod Shader Loading Before Removal

**Add as Phase 3.5**:
- Build project with test mod
- Verify shaders compile
- Test `LoadModShader()` with test shader IDs
- Verify shaders work in rendering

### 3. Update IShaderService Interface

**Add to Phase 1.3**:
- Update `IShaderService.cs` to add `LoadModShader(string shaderId)` method
- Add XML documentation

### 4. Handle Shader Caching for Mod Shaders

**Add to Phase 1.3**:
- Update `GetShader()` caching to work with mod shader IDs
- Ensure cache keys are unique
- Test cache eviction works correctly

### 5. MSBuild Tool Availability Check

**Add to Phase 2.1**:
- Check if `dotnet mgfxc` is available
- If not, provide clear error message with installation instructions
- Or add tool restore step

## Recommendations Summary

1. **Clarify Phase 4.3**: Remove ContentManager entirely, don't keep backward compatibility
2. **Add Phase 3.5**: Test mod shaders before removing ContentManager
3. **Update IShaderService**: Add `LoadModShader()` to interface
4. **Find Shader Usages**: Add step to discover all call sites
5. **Complete MSBuild**: Provide full PowerShell script implementation
6. **Fix mod.manifest**: Specify exact mod ID and position
7. **Add ID Validation**: Validate shader ID format in `LoadModShader()`
8. **Handle Errors**: Add proper error handling in `ShaderLoader`
9. **Document Parameters**: Specify parameter JSON format clearly
10. **Verify GameServices**: Ensure ModManager available when initializing ShaderService

## Priority Fixes

**High Priority** (Block implementation):
1. Clarify ContentManager removal strategy
2. Add IShaderService interface update
3. Complete MSBuild implementation details
4. Find all shader usages

**Medium Priority** (Cause issues during implementation):
5. Add Phase 3.5 testing step
6. Fix mod.manifest update
7. Add shader ID validation
8. Handle ShaderLoader errors

**Low Priority** (Polish):
9. Document parameter format
10. Verify GameServices initialization order


