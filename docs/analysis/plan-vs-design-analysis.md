# Plan vs Design Analysis

## Overview

This document analyzes the implementation plan against the design document to identify missing elements, discrepancies, and areas that need clarification.

---

## Issues Found

### 1. Missing: Early Return Check Optimization in PreloadAllScripts

**Design Location**: Section 6, lines 1257-1287

**Issue**: The plan mentions adding an early return check, but doesn't specify the optimization details from the design:
- Try-catch around cache check (handle errors gracefully)
- Early exit when finding first uncached script (optimization)
- Proper logging of cached vs compiled counts

**Plan Location**: Step 1.6, line 218

**Fix Required**: Add details about:
- Try-catch wrapper around cache check
- Early exit optimization (break on first uncached)
- Logging both cached and compiled counts

---

### 2. Missing: Progress Reporting

**Design Location**: Phase 1, line 1620-1621

**Issue**: Design mentions "Add progress reporting - Report compilation progress for better UX" but plan doesn't include this step.

**Plan Location**: Phase 1 - not mentioned

**Fix Required**: Add step for progress reporting (optional, but mentioned in design):
- Report compilation progress during PreloadAllScripts
- Could use LoadingSceneSystem for progress updates

---

### 3. Missing: Reduce Debug Logging

**Design Location**: Phase 2, line 1648-1650

**Issue**: Design mentions "Reduce debug logging - Remove logs from hot paths, Use conditional compilation" but plan doesn't include this.

**Plan Location**: Phase 2 - not mentioned

**Fix Required**: Add step to reduce debug logging in hot paths:
- Remove or conditionally compile debug logs in ScriptLifecycleSystem.Update()
- Remove debug logs from ScriptBase.IsEventForThisEntity()

---

### 4. Missing: Script Instance Pooling (Optional)

**Design Location**: Phase 3, line 1668-1670

**Issue**: Design mentions "Script instance pooling (optional) - Pool stateless scripts, Reduce GC pressure" but plan doesn't include this.

**Plan Location**: Phase 3 - not mentioned

**Fix Required**: Add optional step for script instance pooling (can be marked as optional/future)

---

### 5. Missing: Ref Parameter Handling in ScriptBase

**Design Location**: Section 5, lines 1184-1193

**Issue**: Design shows two overloads of `IsEventForThisEntity`:
- `IsEventForThisEntity<TEvent>(TEvent evt)` - for copy
- `IsEventForThisEntity<TEvent>(ref TEvent evt)` - for ref parameter

**Plan Location**: Step 2.4, line 322

**Fix Required**: Plan should mention both overloads need to be updated:
- The ref version copies the struct and calls the copy version
- Both use compiled expression trees

---

### 6. Missing: CollectDependencyAssemblies Method Details

**Design Location**: Section 2, lines 797-894

**Issue**: Design shows `CollectDependencyAssemblies()` needs to track temp files, but plan doesn't specify this detail.

**Plan Location**: Step 1.6, line 238

**Fix Required**: Plan should specify:
- `CollectDependencyAssemblies()` needs to call `_compilationCache.TempFileManager.TrackTempFile()` when creating temp files for compressed mods
- This is already mentioned but could be more explicit

---

### 7. Missing: ScriptChangeTracker Creation Timing

**Design Location**: Step 1 file list, line 1698

**Issue**: Design lists `ScriptChangeTracker.cs` in Phase 1 file creation list, but it's used in Phase 2.

**Plan Location**: Step 2.1 (Phase 2)

**Analysis**: This is actually correct - ScriptChangeTracker is created in Phase 2 when it's needed. The design's file list is just organizational. No fix needed.

---

### 8. Missing: Helper Method Location Clarification

**Design Location**: Section 3.1 (Phase 3)

**Issue**: Design mentions helper method for script attachment, but doesn't specify exact file location.

**Plan Location**: Step 3.1, line 337

**Fix Required**: Plan correctly says "File to create or modify" but should specify:
- Prefer creating new file: `MonoBall.Core/ECS/Utilities/ScriptAttachmentHelper.cs`
- Or add to existing utility if appropriate

---

### 9. Missing: Dispose Pattern Details

**Design Location**: Section 2, lines 899-904

**Issue**: Design shows `ScriptLoaderService.Dispose()` should NOT clear the shared cache, but plan doesn't specify what it should do.

**Plan Location**: Step 1.6, line 242

**Fix Required**: Plan should specify:
- Dispose should only cleanup plugin scripts
- Do NOT call `_compilationCache.Clear()` or dispose the cache
- Temp files are cleaned up by TempFileManager.Dispose() (called when game exits)

---

### 10. Missing: Error Handling in Factory Cache

**Design Location**: Section 1.2, ScriptFactoryCache implementation

**Issue**: Design shows factory creation can return null if compilation fails, but plan doesn't mention error handling.

**Plan Location**: Step 1.2

**Fix Required**: Plan should mention:
- `GetOrCreateFactory()` returns `Func<ScriptBase>?` (nullable)
- Returns null if factory creation fails (e.g., no parameterless constructor)
- `CreateScriptInstance()` should handle null factory with clear exception

---

### 11. Missing: TempFileManager.Dispose Implementation

**Design Location**: Section 1.2, TempFileManager, lines 471-479

**Issue**: Plan mentions TempFileManager implements IDisposable but doesn't specify cleanup details.

**Plan Location**: Step 1.2, Step 3.3

**Fix Required**: Plan should specify:
- `Dispose()` calls `CleanupAllTempFiles()`
- Clears the `_tempFiles` dictionary
- Sets `_disposed` flag

---

### 12. Missing: ScriptChangeTracker Initial State

**Design Location**: Section 4, line 950

**Issue**: Design shows `_isDirty = true` initially (to ensure first query), but plan doesn't mention this.

**Plan Location**: Step 2.1, line 274

**Fix Required**: Plan should specify:
- `_isDirty` starts as `true` (not `false`)
- This ensures first Update() call processes existing entities
- Document why it starts dirty

---

### 13. Missing: EntityCreatedEvent Handler Details

**Design Location**: Section 4, lines 1092-1096

**Issue**: Design shows `OnEntityCreated()` should check if entity has `ScriptAttachmentComponent` before marking dirty, but plan doesn't specify this check.

**Plan Location**: Step 2.2, line 295

**Fix Required**: Plan should specify:
- `OnEntityCreated()` should check `World.Has<ScriptAttachmentComponent>(evt.Entity)` before calling `MarkDirty()`
- Only mark dirty if entity actually has scripts

---

### 14. Missing: ScriptAttachmentData Type

**Design Location**: Section 3.1, helper method signature

**Issue**: Plan mentions `ScriptAttachmentData` but doesn't specify where this type comes from.

**Plan Location**: Step 3.1, line 346

**Fix Required**: Plan should specify:
- `ScriptAttachmentData` is likely in `ScriptAttachmentComponent` or related namespace
- Verify the exact type name and location before implementation

---

### 15. Missing: Parallel.ForEach Error Handling

**Design Location**: Section 2, lines 651-658

**Issue**: Design shows try-catch around individual script compilation in parallel loop, but plan doesn't specify error handling strategy.

**Plan Location**: Step 1.6, line 219

**Fix Required**: Plan should specify:
- Each script compilation should be wrapped in try-catch
- Errors should be logged but not stop parallel compilation
- Failed scripts are skipped, successful ones continue

---

## Minor Issues / Clarifications

### 16. ScriptChangeTracker File Location

**Design**: `MonoBall.Core/ECS/ScriptChangeTracker.cs`
**Plan**: `MonoBall.Core/ECS/ScriptChangeTracker.cs`
**Status**: ✅ Match

### 17. Helper Method Location

**Design**: Mentions helper in GameInitializationHelper or MonoBallGame
**Plan**: GameInitializationHelper
**Status**: ✅ Correct (GameInitializationHelper is better location)

### 18. Cache Registration Location

**Design**: `LoadContent()`, after `LoadModsSynchronously()` but before SystemManager
**Plan**: `LoadContent()`, after `LoadModsSynchronously()` but before SystemManager
**Status**: ✅ Match

---

## Summary of Required Fixes

### High Priority (Must Fix)

1. ✅ Add early return check optimization details (try-catch, early exit)
2. ✅ Add ref parameter handling in ScriptBase.IsEventForThisEntity
3. ✅ Add error handling details for factory cache (nullable return)
4. ✅ Add ScriptChangeTracker initial state (_isDirty = true)
5. ✅ Add EntityCreatedEvent handler check (verify entity has component)

### Medium Priority (Should Fix)

6. ✅ Add progress reporting step (optional but mentioned in design)
7. ✅ Add reduce debug logging step
8. ✅ Add script instance pooling (optional/future)
9. ✅ Add Dispose pattern details (what to cleanup, what not to)
10. ✅ Add temp file cleanup details in TempFileManager.Dispose

### Low Priority (Nice to Have)

11. ✅ Clarify ScriptAttachmentData type location
12. ✅ Add parallel compilation error handling details

---

## Recommended Plan Updates

### Update Step 1.6 (PreloadAllScripts)

Add:
- Try-catch around cache check with warning log
- Early exit optimization (break on first uncached)
- Logging of cached vs compiled counts
- Error handling in parallel loop (try-catch per script)

### Update Step 1.2 (Cache Implementations)

Add:
- ScriptFactoryCache returns nullable `Func<ScriptBase>?`
- TempFileManager.Dispose() implementation details
- Error handling for factory compilation failures

### Update Step 2.1 (ScriptChangeTracker)

Add:
- `_isDirty` starts as `true` (not `false`)
- Document why it starts dirty

### Update Step 2.2 (ScriptLifecycleSystem)

Add:
- `OnEntityCreated()` checks `World.Has<ScriptAttachmentComponent>()` before marking dirty
- Reduce debug logging in Update() method

### Update Step 2.4 (ScriptBase)

Add:
- Both overloads need updating (copy and ref versions)
- Ref version copies struct and calls copy version

### Add New Steps

- **Step 1.9**: Add progress reporting (optional)
- **Step 2.6**: Reduce debug logging in hot paths
- **Step 3.5**: Script instance pooling (optional/future)

---

## Conclusion

The plan is **mostly complete** but missing several important details from the design:

1. **Error handling details** - try-catch patterns, nullable returns
2. **Optimization details** - early exit, progress reporting
3. **Initialization details** - ScriptChangeTracker starts dirty
4. **Disposal details** - what to cleanup, what not to cleanup
5. **Optional features** - progress reporting, debug logging reduction, script pooling

Most issues are **missing details** rather than **incorrect information**, which means the plan is on the right track but needs refinement.
