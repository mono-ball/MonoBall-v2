# Cake Frosting Build System - Plan vs Design Analysis

## Executive Summary

This document analyzes the implementation plan against the design document to identify issues, gaps, inconsistencies, and missing requirements.

---

## Critical Issues

### 1. Missing DefaultTask Class ⚠️ **CRITICAL**

**Issue:** The design shows a "Default" task in the dependency graph (Section 2.3, Appendix A), but the plan doesn't specify creating a `DefaultTask` class.

**Design Reference:**
- Section 2.3 shows: `Default → Publish → CopyMods → ...`
- Appendix A shows Default task at the root of dependency tree

**Plan Gap:**
- Phase 6 says "Set Default task to Publish" but doesn't explain how
- No mention of creating `DefaultTask.cs` file

**Required Fix:**
Add to Phase 5 or Phase 6:
- Create `.build/MonoBall.Build/Tasks/DefaultTask.cs`
- Task should depend on PublishTask
- Use `[TaskName("Default")]` attribute
- Full XML documentation

**Code Structure:**
```csharp
namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Default task that runs the full build and publish pipeline.
    /// </summary>
    [TaskName("Default")]
    [IsDependentOn(typeof(PublishTask))]
    public sealed class DefaultTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the default task.
        /// </summary>
        /// <param name="context">The build context.</param>
        public override void Run(BuildContext context)
        {
            context.Log.Information("Default task completed successfully.");
        }
    }
}
```

---

### 2. Bootstrapping Script Path Issue ⚠️ **HIGH**

**Issue:** Bootstrapping scripts reference `.build/MonoBall.Build/MonoBall.Build.csproj`, but if scripts are in `.build/` directory, this path is incorrect.

**Current Plan:**
- Scripts location: `.build/build.ps1` and `.build/build.sh`
- Scripts call: `dotnet cake .build/MonoBall.Build/MonoBall.Build.csproj`

**Problem:**
- If scripts are in `.build/`, the path `.build/MonoBall.Build/...` would be looking for `.build/.build/MonoBall.Build/...`
- Should use relative path: `MonoBall.Build/MonoBall.Build.csproj`
- Or use absolute path resolution

**Design Reference:**
- Section 5.1 and 5.2 show scripts calling `dotnet cake .build/MonoBall.Build/MonoBall.Build.csproj`
- But design doesn't specify script location

**Required Fix:**
**Option A:** Scripts in root directory (recommended)
- Move scripts to root: `build.ps1` and `build.sh`
- Keep path as `.build/MonoBall.Build/MonoBall.Build.csproj`

**Option B:** Scripts in `.build/` directory
- Keep scripts in `.build/`
- Change path to: `MonoBall.Build/MonoBall.Build.csproj`
- Or use: `$(dirname "$0")/MonoBall.Build/MonoBall.Build.csproj`

**Recommendation:** Use Option A (scripts in root) as it's more conventional and matches design examples.

---

### 3. CompileShadersTask Dependency Chain ⚠️ **MEDIUM**

**Issue:** Plan shows CompileShadersTask depends on RestoreTask, but design dependency chain shows it could run in parallel with BuildArchiveToolTask.

**Design Dependency Chain:**
```
Restore
    ↓
BuildArchiveTool  (parallel branch)
    ↓
CompileShaders   (parallel branch)
    ↓
CompressMods (needs both)
```

**Plan Current:**
- CompileShadersTask depends on RestoreTask ✅
- BuildArchiveToolTask depends on RestoreTask ✅
- CompressModsTask depends on BuildArchiveToolTask ✅

**Analysis:**
- Plan is actually correct - both can depend on RestoreTask independently
- CompressModsTask needs BuildArchiveToolTask (correct)
- CompressModsTask doesn't need CompileShadersTask directly, but design shows CompileShaders before CompressMods

**Required Clarification:**
- Should CompressModsTask also depend on CompileShadersTask?
- Design shows: `CompressMods → CompileShaders → Restore` (backwards)
- But also shows: `BuildArchiveTool → CompileShaders → Restore` (backwards)

**Actual Design Intent (from Section 2.3):**
```
CompressMods
    ↓
BuildArchiveTool
    ↓
CompileShaders
    ↓
Restore
```

**Fix:** CompressModsTask should depend on both BuildArchiveToolTask AND CompileShadersTask, or CompileShadersTask should depend on BuildArchiveToolTask. The plan needs to clarify this.

---

### 4. Missing PublishDirectory Initialization ⚠️ **MEDIUM**

**Issue:** Plan doesn't mention initializing `PublishDirectory` in BuildContext, but it's used in PublishTask.

**Design Reference:**
- BuildContext has `PublishDirectory?` property
- PublishTask uses it

**Plan Gap:**
- Phase 2 doesn't mention initializing PublishDirectory
- Should be initialized in BuildContext constructor based on publish output path

**Required Addition:**
Add to Phase 2 (BuildContext implementation):
- Initialize `PublishDirectory` based on publish output location
- Typically: `bin/<Configuration>/<TargetFramework>/publish/` or similar
- Can be null until publish is run

---

### 5. Missing TestTask Implementation ⚠️ **LOW**

**Issue:** Design mentions TestTask (Section 3.8) as "Future", but plan doesn't include it at all.

**Design Reference:**
- Section 3.8: "TestTask (Future)"
- Section 2.1: TestTask.cs listed in file structure
- BuildContext has `SkipTests` flag

**Plan Gap:**
- No mention of TestTask implementation
- Even as a placeholder or future task

**Required Addition:**
Add to Phase 5 or create separate phase:
- Create `.build/MonoBall.Build/Tasks/TestTask.cs` (can be placeholder)
- Or document as "Future Enhancement" in plan

---

### 6. Bootstrapping Script Execute Permissions ⚠️ **LOW**

**Issue:** Plan mentions "with execute permissions" for build.sh but doesn't specify how to set them.

**Required Addition:**
Add to Phase 1:
- After creating `build.sh`, run: `chmod +x build.sh`
- Or document that developers need to set permissions manually

---

## Minor Issues

### 7. Program.cs Default Task Configuration

**Issue:** Plan says "Set Default task to Publish" but Cake Frosting doesn't work that way.

**Clarification Needed:**
- Cake Frosting uses `[TaskName("Default")]` attribute on a task class
- Or you can use `.SetDefaultTask<T>()` in Program.cs
- Plan should specify which approach

**Recommended:** Use `DefaultTask` class with `[TaskName("Default")]` attribute (more explicit and follows Cake conventions)

---

### 8. Missing Error Handling Details

**Issue:** Plan mentions "fail-fast error handling" but doesn't specify exception types for each task.

**Design Reference:**
- Each task has specific exception types documented
- CleanTask: `ArgumentNullException`
- CompileShadersTask: `ArgumentNullException`, `InvalidOperationException`
- CompressModsTask: `ArgumentNullException`, `FileNotFoundException`, `InvalidOperationException`

**Required Addition:**
- Add exception type specifications to each task phase
- Reference design document for specific exceptions

---

### 9. Missing Constants Documentation

**Issue:** Plan mentions constants but doesn't list all required constants for each task.

**Required Constants:**
- BuildContext: `DefaultConfiguration`, `DebugConfiguration`, `ReleaseConfiguration`, `DefaultTargetFramework`
- CompileShadersTask: `ShaderExtension`, `CompiledShaderExtension`, `ShaderProfile`
- CompressModsTask: `ArchiveExtension`, `CompressionLevel`

**Required Addition:**
- List all constants in each task phase
- Ensure they match design document

---

### 10. GitHub Actions Workflow Details Missing

**Issue:** Plan mentions creating workflow but doesn't include caching configuration details.

**Design Reference:**
- Section 10.2 shows caching strategy
- Section 11.1 mentions caching in GitHub Actions

**Required Addition:**
Add to Phase 9:
- NuGet package caching
- Dotnet tools caching
- Cache key strategies
- Artifact upload/download steps

---

## Summary of Required Fixes

### Critical (Must Fix)
1. ✅ Add DefaultTask class creation to plan
2. ✅ Fix bootstrapping script path issue (move to root or fix relative path)
3. ✅ Clarify CompileShadersTask dependency relationship with CompressModsTask

### High Priority
4. ✅ Add PublishDirectory initialization to BuildContext phase
5. ✅ Specify error handling exception types for each task

### Medium Priority
6. ✅ Add TestTask placeholder or future enhancement note
7. ✅ Add constants list to each task phase
8. ✅ Add GitHub Actions caching details

### Low Priority
9. ✅ Document build.sh execute permissions setup
10. ✅ Clarify Program.cs default task configuration approach

---

## Plan Compliance Checklist

- [x] All tasks from design are included
- [x] Task dependencies match design
- [x] File structure matches design
- [ ] DefaultTask class creation specified
- [ ] Bootstrapping script paths are correct
- [ ] All constants are documented
- [ ] Exception types are specified
- [ ] GitHub Actions caching is detailed
- [ ] TestTask is addressed (even as future)
- [ ] PublishDirectory initialization is included

---

## Recommended Plan Updates

### Update Phase 1
- Clarify bootstrapping script location (recommend root directory)
- Add `chmod +x build.sh` step

### Update Phase 2
- Add PublishDirectory initialization
- List all constants that need to be defined

### Update Phase 5
- Add DefaultTask class creation
- Add TestTask placeholder (or separate "Future" phase)
- List all constants for each task
- Specify exception types for each task

### Update Phase 6
- Clarify that DefaultTask class handles default, not Program.cs configuration
- Or specify `.SetDefaultTask<PublishTask>()` if using Program.cs approach

### Update Phase 9
- Add detailed caching configuration
- Add artifact upload/download steps
- Reference design Section 10.2 for caching strategy

---

## Conclusion

The plan is **mostly complete** but has several critical gaps that need to be addressed:

1. **Missing DefaultTask** - Critical for Cake Frosting to work correctly
2. **Bootstrapping script path** - Will cause runtime errors
3. **Dependency clarification** - Need to verify CompileShadersTask relationship

Once these issues are fixed, the plan will be ready for implementation.
