# Plan Analysis: Compressed Mod Support Implementation

**Generated:** 2025-01-16  
**Status:** Plan Review  
**Scope:** Comparison of implementation plan vs design document

---

## Executive Summary

This document analyzes the implementation plan against the design document to identify missing elements, discrepancies, and areas that need clarification before implementation begins.

---

## Critical Issues Found

### 1. ❌ ModValidator.ValidateAll() Not Fully Covered

**Problem**: The plan mentions updating `CollectDefinitionIds()`, but `ValidateAll()` also needs significant updates.

**Current Code Issues**:
- Line 55: Uses `Directory.GetDirectories()` - won't find `.monoball` archives
- Line 61-77: Iterates over directories, reads `mod.json` with `File.ReadAllText()` - won't work for archives
- Line 125: Calls `CollectDefinitionIds(manifest, modDir, ...)` - needs `IModSource` instead
- Line 454-456: `ValidateShaderDefinition()` uses `Path.Combine(modDir, ...)` and `File.Exists()` - won't work for archives

**Required Updates** (Missing from Plan):
1. **Update `ValidateAll()` method**:
   - Discover both directories AND archives (similar to `ModLoader.DiscoverMods()`)
   - Create `IModSource` instances for each mod
   - Use `modSource.ReadTextFile("mod.json")` instead of `File.ReadAllText()`
   - Pass `manifest.ModSource` to `CollectDefinitionIds()` instead of `modDir`

2. **Update `ValidateShaderDefinition()` signature**:
   - Change from `(string definitionPath, JsonDocument jsonDoc, string modDir, ...)`
   - To: `(string definitionPath, JsonDocument jsonDoc, IModSource modSource, ...)`
   - Replace `Path.Combine(modDir, sourceFile)` with `modSource.FileExists(sourceFile)`
   - Update call site at line 304

**Plan Section**: Phase 3.3 needs expansion

---

### 2. ⚠️ Missing Details in LoadModDefinitions() Update

**Problem**: Plan mentions updating `LoadModDefinitions()` but doesn't specify the exact path filtering logic.

**Design Shows** (Lines 1012-1016):
```csharp
var jsonFiles = mod.ModSource.EnumerateFiles("*.json", SearchOption.AllDirectories)
    .Where(p => p.StartsWith(relativePath + "/", StringComparison.Ordinal) || 
               p == relativePath || 
               p.StartsWith(relativePath + "\\", StringComparison.Ordinal))
    .ToList();
```

**Plan Says**: "Replace `Directory.GetFiles()` with `mod.ModSource.EnumerateFiles()`"

**Required Clarification**: Plan should specify the exact `Where()` filtering logic for content folders, script definitions, and behavior definitions.

---

### 3. ⚠️ ArchiveModSource.GetTOC() Visibility

**Problem**: Plan doesn't mention that `GetTOC()` is `internal` (for testing/validation).

**Design Shows** (Line 393): `internal Dictionary<string, FileEntry> GetTOC()`

**Plan Says**: "Lazy TOC loading with integrity validation"

**Required Addition**: Note that `GetTOC()` is `internal` and called during discovery for early validation.

---

### 4. ⚠️ ReaderWriterLockSlim Disposal

**Problem**: Plan mentions using `ReaderWriterLockSlim` but doesn't mention disposing it.

**Design Shows** (Line 697): `_tocLock.Dispose();` in `Dispose()` method

**Plan Says**: "Uses `ReaderWriterLockSlim` for TOC access"

**Required Addition**: Note that `ReaderWriterLockSlim` must be disposed in `Dispose()` method.

---

### 5. ⚠️ Static Regex Cache Details

**Problem**: Plan mentions caching regex patterns but doesn't specify it's static.

**Design Shows** (Lines 354-355):
```csharp
private static readonly Dictionary<string, Regex> _patternCache = new();
private static readonly object _patternCacheLock = new();
```

**Plan Says**: "Caches compiled regex patterns for `EnumerateFiles()`"

**Required Clarification**: Specify that the cache is static (shared across instances) with a static lock.

---

### 6. ⚠️ ModValidator.ValidateAll() Discovery Logic

**Problem**: `ModValidator.ValidateAll()` currently only discovers directories. Plan doesn't mention updating the discovery logic.

**Current Code** (Line 55): `var modDirectories = Directory.GetDirectories(_modsDirectory);`

**Required Update**: Similar to `ModLoader.DiscoverMods()`, need to:
- Discover directories: `Directory.GetDirectories()`
- Discover archives: `Directory.GetFiles(_modsDirectory, "*.monoball")`
- Create `IModSource` instances for both

**Plan Section**: Phase 3.3 needs expansion

---

### 7. ⚠️ ModValidator Constructor May Need Update

**Problem**: `ModValidator` constructor takes `string modsDirectory`. May need to accept `IModManager` or discover mods differently.

**Current Code** (Line 25-30):
```csharp
public ModValidator(string modsDirectory, ILogger logger)
{
    _modsDirectory = modsDirectory ?? throw new ArgumentNullException(nameof(modsDirectory));
    // ...
}
```

**Design Implication**: `ValidateAll()` needs to discover mods similar to `ModLoader`, which means it needs access to mod discovery logic.

**Options**:
1. Keep `modsDirectory` parameter, duplicate discovery logic from `ModLoader`
2. Accept `IModManager` and use already-discovered mods
3. Extract discovery logic to shared utility

**Plan Section**: Phase 3.3 needs decision on approach

---

### 8. ⚠️ DefinitionLocation.FilePath Format

**Problem**: `ModValidator.CollectDefinitionIds()` stores `FilePath` as absolute path (line 295: `FilePath = jsonFile`). With archives, this should be relative path.

**Current Code** (Line 295): `FilePath = jsonFile` (absolute path from `Directory.GetFiles()`)

**Design Shows** (Line 1114): `SourcePath = relativePath` (relative path from `EnumerateFiles()`)

**Required Update**: `DefinitionLocation.FilePath` should store relative path when using `IModSource`.

**Plan Section**: Phase 3.3 needs clarification

---

### 9. ⚠️ Missing Using Statements in Plan

**Problem**: Plan doesn't list all required `using` statements for new files.

**Design Shows** (ArchiveModSource, Lines 321-332):
- `using System.Buffers;` (for ArrayPool)
- `using System.Text.RegularExpressions;` (for Regex)
- `using K4os.Compression.LZ4;` (for LZ4Codec)
- `using MonoBall.Core.Mods.Utilities;` (for ModPathNormalizer, ModManifestLoader)
- `using System.Threading;` (for ReaderWriterLockSlim)

**Plan Says**: "Implement ArchiveModSource" but doesn't list using statements

**Required Addition**: List all required `using` statements in implementation details

---

### 10. ⚠️ ArchiveModSource Constructor Validation

**Problem**: Plan doesn't mention that constructor validates file existence.

**Design Shows** (Lines 367-370):
```csharp
if (!File.Exists(archivePath))
{
    throw new FileNotFoundException($"Archive not found: {archivePath}");
}
```

**Plan Says**: "Initializes a new instance of ArchiveModSource"

**Required Addition**: Note that constructor validates file exists and throws if not found

---

## Minor Issues / Clarifications Needed

### 11. ⚠️ LoadDefinitionFromFile() Error Handling

**Problem**: Plan mentions extracting `LoadDefinitionFromFile()` but doesn't specify error handling details.

**Design Shows** (Lines 1136-1147): Try-catch blocks for `JsonException` and general `Exception`, logging errors

**Plan Says**: "Extract `LoadDefinitionFromFile()` helper method"

**Required Clarification**: Specify error handling (try-catch, logging, error list population)

---

### 12. ⚠️ SourcePath in DefinitionMetadata

**Problem**: Plan mentions updating `SourcePath` but doesn't clarify the format change.

**Current Code**: `SourcePath = Path.GetRelativePath(mod.ModDirectory, jsonFile)` (absolute to relative conversion)

**Design Shows** (Line 1114): `SourcePath = relativePath` (already relative from `EnumerateFiles()`)

**Required Clarification**: Note that `SourcePath` is now always relative (no conversion needed)

---

### 13. ⚠️ ModValidator.ValidateShaderDefinition() File Path

**Problem**: `ValidateShaderDefinition()` uses absolute path construction. Needs update for archives.

**Current Code** (Line 454): `var mgfxoPath = Path.Combine(modDir, sourceFile);`

**Design Implication**: Should use `modSource.FileExists(sourceFile)` instead

**Required Update**: Update `ValidateShaderDefinition()` to use `IModSource.FileExists()`

**Plan Section**: Phase 3.3 needs this detail

---

## Missing from Plan

### 14. ❌ ModValidator.ValidateAll() Discovery Update

**Missing**: Complete rewrite of `ValidateAll()` discovery logic to handle both directories and archives.

**Required**:
- Discover directories (create `DirectoryModSource`)
- Discover archives (create `ArchiveModSource`)
- Use `IModSource` for all operations
- Update all call sites

---

### 15. ⚠️ ArchiveModSource Internal GetTOC() Usage

**Missing**: Plan doesn't mention that `GetTOC()` is called during discovery for early validation.

**Design Shows** (Lines 921-925):
```csharp
if (source is ArchiveModSource archiveSource)
{
    _ = archiveSource.GetTOC(); // Trigger TOC load, catch errors
}
```

**Required Addition**: Note that `GetTOC()` is called during discovery to validate archive integrity early

---

## Plan Strengths

✅ **Good Coverage**: Plan covers all major components  
✅ **Clear Phases**: Well-organized into logical phases  
✅ **Dependencies**: Correctly identifies dependencies  
✅ **Critical Notes**: Highlights critical implementation notes  
✅ **Testing Strategy**: Includes testing approach  

---

## Summary of Required Plan Updates

### Critical (Must Fix):

1. **Expand Phase 3.3**: Add complete `ModValidator.ValidateAll()` update
   - Discovery logic for directories and archives
   - Update `CollectDefinitionIds()` signature and implementation
   - Update `ValidateShaderDefinition()` signature and implementation
   - Update all call sites

2. **Clarify Phase 2.3**: Specify exact path filtering logic in `LoadModDefinitions()`
   - Content folder filtering with `StartsWith()` checks
   - Script/Behavior definition path filtering

### Important (Should Fix):

3. **Add Implementation Details**: 
   - List all `using` statements for new files
   - Note `GetTOC()` is `internal` and called during discovery
   - Note `ReaderWriterLockSlim` disposal in `Dispose()`
   - Note static regex cache with static lock

4. **Clarify ModValidator Approach**:
   - Decide: duplicate discovery logic vs use `IModManager` vs shared utility
   - Update `DefinitionLocation.FilePath` format (relative vs absolute)

### Nice to Have:

5. **Error Handling Details**: Specify error handling in `LoadDefinitionFromFile()`
6. **SourcePath Format**: Clarify that `SourcePath` is now always relative

---

## Recommended Plan Updates

### Update Phase 3.3:

```markdown
### 3.3 Update ModValidator
- **File**: `MonoBall/MonoBall.Core/Mods/ModValidator.cs`
- **CRITICAL**: Update `ValidateAll()` discovery logic:
  - Discover directories: Create `DirectoryModSource` for each directory
  - Discover archives: Find `*.monoball` files, create `ArchiveModSource` for each
  - Use `modSource.ReadTextFile("mod.json")` instead of `File.ReadAllText()`
  - Pass `manifest.ModSource` to `CollectDefinitionIds()` instead of `modDir`
- Update `CollectDefinitionIds()` signature: Accept `IModSource` instead of `string modDir`
  - Replace `Directory.GetFiles()` with `modSource.EnumerateFiles()`
  - Replace `File.ReadAllText()` with `modSource.ReadTextFile()`
  - Update `DefinitionLocation.FilePath` to store relative path (from `EnumerateFiles()`)
- Update `ValidateShaderDefinition()` signature: Accept `IModSource` instead of `string modDir`
  - Replace `Path.Combine(modDir, sourceFile)` with `modSource.FileExists(sourceFile)`
  - Update call site at line 304
```

### Update Phase 2.3:

```markdown
### 2.3 Update LoadModDefinitions()
- **File**: `MonoBall/MonoBall.Core/Mods/ModLoader.cs`
- **CRITICAL**: Replace `Directory.GetFiles()` with `mod.ModSource.EnumerateFiles()`
- **Path Filtering Logic**:
  - Content folders: Filter with `p.StartsWith(relativePath + "/") || p == relativePath || p.StartsWith(relativePath + "\\")`
  - Script definitions: Filter with `p.StartsWith("Definitions/Scripts/") || p.StartsWith("Definitions\\Scripts\\")`
  - Behavior definitions: Filter with `p.StartsWith("Definitions/Behaviors/") || p.StartsWith("Definitions\\Behaviors\\")`
- Replace `File.ReadAllText()` with `mod.ModSource.ReadTextFile()`
- Replace `Path.GetRelativePath(mod.ModDirectory, jsonFile)` with relative path from `EnumerateFiles()`
- Extract `LoadDefinitionFromFile()` helper method that takes `IModSource` and relative path
  - Include error handling: try-catch for `JsonException` and general `Exception`
  - Log errors and add to errors list
```

### Add to Phase 1.5:

```markdown
- **Internal `GetTOC()` method**: `internal Dictionary<string, FileEntry> GetTOC()` for testing/validation
  - Called during discovery for early archive validation
- **Dispose `ReaderWriterLockSlim`**: Call `_tocLock.Dispose()` in `Dispose()` method
- **Static regex cache**: `private static readonly Dictionary<string, Regex> _patternCache` with static lock
```

---

## Conclusion

The plan is **mostly complete** but needs **critical updates** to Phase 3.3 (ModValidator) and **clarifications** to Phase 2.3 (LoadModDefinitions path filtering). Once these are addressed, the plan will be ready for implementation.

