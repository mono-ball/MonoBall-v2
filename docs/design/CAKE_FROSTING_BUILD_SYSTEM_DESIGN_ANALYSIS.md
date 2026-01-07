# Cake Frosting Build System Design - Architecture & Integration Analysis

## Executive Summary

This document analyzes the proposed Cake Frosting build system design for architectural issues, GitHub Actions integration problems, cross-platform compatibility concerns, and other potential pitfalls. The analysis identifies critical issues that must be addressed before implementation.

---

## 1. Critical Architecture Issues

### 1.1 Task Dependency Order Problem ⚠️ **CRITICAL**

**Issue:** `CompressModsTask` requires `MonoBall.ArchiveTool` to be built, but `BuildTask` (which builds all projects including ArchiveTool) comes **after** `CompressModsTask` in the dependency chain.

**Current Dependency Chain:**
```
CompressModsTask (needs ArchiveTool)
    ↓
BuildTask (builds ArchiveTool)
```

**Problem:** ArchiveTool must be built before it can be used to compress mods.

**Solution:** Split build into two phases:
1. **BuildArchiveToolTask** - Build only ArchiveTool (early in chain)
2. **BuildTask** - Build remaining projects (after mod compression)

**Corrected Dependency Chain:**
```
RestoreTask
    ↓
BuildArchiveToolTask  ← NEW: Build ArchiveTool early
    ↓
CompileShadersTask
    ↓
CompressModsTask (can now use ArchiveTool)
    ↓
BuildTask (builds Core + DesktopGL)
    ↓
CopyModsTask
```

### 1.2 Build Directory Naming Conflict ⚠️ **HIGH**

**Issue:** Using `build/MonoBall.Build/` directory conflicts with MSBuild's standard `build/` output directory convention and may cause confusion.

**Problem:**
- MSBuild often uses `build/` for intermediate outputs
- Developers might expect `build/` to contain build artifacts
- Could conflict with existing build infrastructure

**Solution Options:**
1. **Option A:** Use `.build/` (hidden directory) - clearly indicates build system
2. **Option B:** Use `build-system/` - more explicit
3. **Option C:** Use `tools/build/` - follows MonoGame convention

**Recommendation:** Use `.build/` to avoid conflicts and follow common Cake Frosting conventions.

### 1.3 Bootstrapping Script Global Tool Installation ⚠️ **HIGH**

**Issue:** Bootstrapping scripts install `Cake.Frosting.Tool` globally, which can cause:
- Version conflicts between projects
- Permission issues on CI systems
- Inconsistent behavior across environments

**Current Approach:**
```bash
dotnet tool install --global Cake.Frosting.Tool --version 4.0.0
```

**Problems:**
1. Global installation requires admin/sudo permissions
2. Version conflicts if multiple projects use different Cake versions
3. CI systems may not allow global tool installation
4. No version pinning enforcement

**Solution:** Use local tool installation via `dotnet-tools.json`:

**Updated `dotnet-tools.json`:**
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "cake.frosting.tool": {
      "version": "4.0.0",
      "commands": ["dotnet-cake"]
    }
  }
}
```

**Updated Bootstrapping Scripts:**
```bash
# Restore tools (including Cake Frosting)
dotnet tool restore

# Run build using local tool
dotnet cake build/MonoBall.Build/MonoBall.Build.csproj -- "$@"
```

### 1.4 BuildContext Initialization Missing Details ⚠️ **MEDIUM**

**Issue:** BuildContext constructor implementation is not specified, leading to potential:
- Path resolution failures
- Null reference exceptions
- Inconsistent behavior across platforms

**Missing Implementation:**
```csharp
public BuildContext(ICakeContext context)
    : base(context)
{
    // Initialize from arguments and environment
    // ❌ No actual implementation provided
}
```

**Required Implementation:**
```csharp
public BuildContext(ICakeContext context)
    : base(context)
{
    // Build Configuration
    Configuration = context.Argument("configuration", "Release");
    TargetFramework = context.Argument("target-framework", "net10.0");

    // Path Resolution (relative to build project)
    var buildProjectDir = context.MakeAbsolute(context.Directory("./build/MonoBall.Build"));
    RootDirectory = buildProjectDir.Combine("../../").Collapse();
    SolutionPath = RootDirectory.CombineWithFilePath("MonoBall.sln");

    // Mods Directory (with environment variable override)
    var modsDir = context.EnvironmentVariable("MONOBALL_MODS_DIR");
    ModsDirectory = modsDir != null
        ? context.MakeAbsolute(context.Directory(modsDir))
        : RootDirectory.Combine("Mods");

    // Output Directory
    var outputDir = context.EnvironmentVariable("MONOBALL_OUTPUT_DIR");
    OutputDirectory = outputDir != null
        ? context.MakeAbsolute(context.Directory(outputDir))
        : RootDirectory.Combine("MonoBall/MonoBall.DesktopGL/bin")
                      .Combine(Configuration)
                      .Combine(TargetFramework);

    // Project Paths
    CoreProjectPath = RootDirectory.CombineWithFilePath("MonoBall/MonoBall.Core/MonoBall.Core.csproj");
    DesktopGLProjectPath = RootDirectory.CombineWithFilePath("MonoBall/MonoBall.DesktopGL/MonoBall.DesktopGL.csproj");
    ArchiveToolProjectPath = RootDirectory.CombineWithFilePath("MonoBall/MonoBall.ArchiveTool/MonoBall.ArchiveTool.csproj");

    // Build Flags
    SkipTests = context.HasArgument("skip-tests");
    SkipShaderCompilation = context.HasArgument("skip-shader-compilation");
    SkipModCompression = context.HasArgument("skip-mod-compression");
    SkipModCopy = context.HasArgument("skip-mod-copy");

    // Validate paths exist
    if (!context.FileExists(SolutionPath))
        throw new FileNotFoundException($"Solution not found: {SolutionPath}");

    if (!context.DirectoryExists(ModsDirectory))
        context.Log.Warning($"Mods directory not found: {ModsDirectory}");
}
```

### 1.5 Path Resolution Issues ⚠️ **MEDIUM**

**Issue:** Relative paths (`../../Mods`) are fragile and break when:
- Running from different directories
- Running in CI/CD environments
- Running from subdirectories

**Problem:** Hard-coded relative paths don't account for:
- Different working directories
- CI/CD runner locations
- Nested solution structures

**Solution:** Always resolve paths relative to build project location or use absolute paths from solution root.

---

## 2. GitHub Actions Integration Issues

### 2.1 Missing GitHub Actions Workflow ⚠️ **CRITICAL**

**Issue:** No GitHub Actions workflow example provided, making CI/CD integration unclear.

**Required Workflow:**
```yaml
name: Build

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]

    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0  # Required for versioning tasks

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          global-json-file: './global.json'

      - name: Restore Tools
        run: dotnet tool restore

      - name: Build
        run: dotnet cake .build/MonoBall.Build/MonoBall.Build.csproj --configuration=Release

      - name: Upload Artifacts
        if: matrix.os == 'windows-latest'
        uses: actions/upload-artifact@v4
        with:
          name: MonoBall-${{ matrix.os }}
          path: |
            MonoBall/MonoBall.DesktopGL/bin/Release/net10.0/publish/**
            Mods/**
```

### 2.2 No Caching Strategy ⚠️ **HIGH**

**Issue:** No caching for NuGet packages, dotnet tools, or build artifacts, leading to:
- Slow CI builds
- Increased bandwidth usage
- Higher CI costs

**Solution:** Add caching steps:
```yaml
- name: Cache NuGet packages
  uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/*.csproj') }}
    restore-keys: |
      ${{ runner.os }}-nuget-

- name: Cache dotnet tools
  uses: actions/cache@v4
  with:
    path: ~/.dotnet/tools
    key: ${{ runner.os }}-tools-${{ hashFiles('**/dotnet-tools.json') }}
    restore-keys: |
      ${{ runner.os }}-tools-
```

### 2.3 No GitHub Actions Context Integration ⚠️ **MEDIUM**

**Issue:** BuildContext doesn't integrate with GitHub Actions environment variables and context.

**Missing Features:**
- GitHub Actions build status reporting
- Artifact upload/download
- Pull request comment integration
- Build matrix support

**Solution:** Add GitHub Actions integration to BuildContext:
```csharp
public bool IsRunningOnGitHubActions => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
public string GitHubSha => Environment.GetEnvironmentVariable("GITHUB_SHA") ?? string.Empty;
public string GitHubRef => Environment.GetEnvironmentVariable("GITHUB_REF") ?? string.Empty;
public bool IsPullRequest => GitHubRef.StartsWith("refs/pull/");
```

### 2.4 No Matrix Build Support ⚠️ **MEDIUM**

**Issue:** Design doesn't account for building on multiple platforms/architectures.

**Missing:** Platform-specific build configurations for:
- Windows (x64, x86)
- Linux (x64, ARM64)
- macOS (x64, ARM64)

**Solution:** Add platform detection and conditional compilation in tasks.

---

## 3. Cross-Platform Compatibility Issues

### 3.1 Executable Extension Handling ⚠️ **MEDIUM**

**Issue:** ArchiveTool executable name differs by platform:
- Windows: `MonoBall.ArchiveTool.exe`
- Linux/macOS: `MonoBall.ArchiveTool`

**Problem:** `CompressModsTask` hardcodes executable name without platform detection.

**Solution:** Use Cake's platform detection:
```csharp
var archiveToolExe = context.IsRunningOnWindows()
    ? context.ArchiveToolProjectPath.GetDirectory().CombineWithFilePath("bin/Release/net10.0/MonoBall.ArchiveTool.exe")
    : context.ArchiveToolProjectPath.GetDirectory().CombineWithFilePath("bin/Release/net10.0/MonoBall.ArchiveTool");
```

### 3.2 Path Separator Issues ⚠️ **LOW**

**Issue:** While Cake handles path separators, bootstrapping scripts might not.

**Problem:** Hard-coded paths in scripts:
```bash
dotnet run --project build/MonoBall.Build/MonoBall.Build.csproj
```

**Solution:** Use Cake's path handling or ensure scripts use forward slashes (work on all platforms with .NET).

### 3.3 Case Sensitivity ⚠️ **LOW**

**Issue:** Linux filesystems are case-sensitive, Windows/macOS are case-insensitive.

**Problem:** Path references might fail on Linux if casing doesn't match exactly.

**Solution:** Ensure all path references match exact casing used in filesystem.

---

## 4. Build System Issues

### 4.1 No Incremental Build Support ⚠️ **MEDIUM**

**Issue:** Every build recompiles shaders and recompresses mods, even if unchanged.

**Problem:**
- Slow iteration times
- Unnecessary work
- No change detection

**Solution:** Add incremental build support:
```csharp
// Check if shader needs recompilation
var shaderNeedsCompile = !context.FileExists(outputPath) ||
    context.GetLastWriteTime(inputPath) > context.GetLastWriteTime(outputPath);

if (shaderNeedsCompile)
{
    // Compile shader
}
```

### 4.2 No Build Validation ⚠️ **MEDIUM**

**Issue:** No validation that required tools are available or paths exist before starting build.

**Problem:**
- Build fails late with unclear errors
- No early validation of prerequisites

**Solution:** Add validation task or validate in BuildContext constructor.

### 4.3 Missing Error Handling Strategy ⚠️ **MEDIUM**

**Issue:** No defined error handling approach for:
- Tool execution failures
- File not found errors
- Permission errors
- Network errors (NuGet restore)

**Solution:** Define error handling strategy:
- Use Cake's built-in error handling
- Add try-catch blocks for critical operations
- Provide clear error messages with context
- Exit with appropriate error codes

### 4.4 No Logging Strategy ⚠️ **LOW**

**Issue:** No defined logging levels or output format.

**Problem:**
- Inconsistent log messages
- Hard to debug build issues
- No structured logging for CI/CD

**Solution:** Use Cake's built-in logging with appropriate verbosity levels.

---

## 5. Task Implementation Issues

### 5.1 CompileShadersTask - Error Handling ⚠️ **MEDIUM**

**Issue:** "Handle errors gracefully" is vague - no specific error handling strategy.

**Problems:**
- What happens if one shader fails?
- Should build continue or fail?
- How are errors reported?

**Solution:** Define error handling:
```csharp
var failedShaders = new List<string>();

foreach (var shaderFile in shaderFiles)
{
    try
    {
        CompileShader(context, shaderFile);
    }
    catch (Exception ex)
    {
        context.Log.Error($"Failed to compile {shaderFile}: {ex.Message}");
        failedShaders.Add(shaderFile.FullPath);
    }
}

if (failedShaders.Any())
{
    throw new Exception($"Failed to compile {failedShaders.Count} shader(s). See errors above.");
}
```

### 5.2 CompressModsTask - ArchiveTool Dependency ⚠️ **CRITICAL**

**Issue:** Task needs ArchiveTool built, but dependency chain doesn't ensure it's built first.

**Solution:** Add `BuildArchiveToolTask` before `CompressModsTask` (see Section 1.1).

### 5.3 CopyModsTask - Race Conditions ⚠️ **LOW**

**Issue:** Deleting and copying mods directory could have race conditions if build runs in parallel.

**Solution:** Use atomic operations or file locking if parallel builds are supported.

---

## 6. Missing Features

### 6.1 No Version Management ⚠️ **MEDIUM**

**Issue:** No task for versioning, Git tagging, or release management.

**Missing:**
- Version extraction from Git tags
- Assembly version patching
- Release notes generation

### 6.2 No Code Quality Tasks ⚠️ **MEDIUM**

**Issue:** No tasks for:
- Code formatting (CSharpier)
- Linting/static analysis
- Code coverage
- Security scanning

### 6.3 No Packaging Tasks ⚠️ **LOW**

**Issue:** No tasks for:
- Creating distributable packages
- Creating installers
- Creating archives for distribution

---

## 7. Recommended Fixes Priority

### Critical (Must Fix Before Implementation)
1. ✅ Fix task dependency order (BuildArchiveToolTask before CompressModsTask)
2. ✅ Fix bootstrapping script global tool installation
3. ✅ Add GitHub Actions workflow example
4. ✅ Complete BuildContext initialization implementation

### High Priority (Fix Soon)
1. ✅ Resolve build directory naming conflict
2. ✅ Add caching strategy for CI/CD
3. ✅ Add path resolution robustness
4. ✅ Add ArchiveTool executable platform detection

### Medium Priority (Fix Before Production)
1. ✅ Add incremental build support
2. ✅ Add build validation
3. ✅ Define error handling strategy
4. ✅ Add GitHub Actions context integration

### Low Priority (Nice to Have)
1. ✅ Add version management tasks
2. ✅ Add code quality tasks
3. ✅ Add packaging tasks

---

## 8. Updated Architecture Recommendations

### 8.1 Corrected Task Dependency Chain

```
Default
    ↓
Publish
    ↓
CopyMods
    ↓
Build (Core + DesktopGL)
    ↓
CompressMods
    ↓
BuildArchiveTool  ← NEW: Build ArchiveTool early
    ↓
CompileShaders
    ↓
Restore
    ↓
Clean
```

### 8.2 Updated Project Structure

```
MonoBall/
├── .build/                          ← Changed from build/
│   ├── MonoBall.Build/
│   │   ├── Program.cs
│   │   ├── BuildContext.cs
│   │   ├── Tasks/
│   │   │   ├── CleanTask.cs
│   │   │   ├── RestoreTask.cs
│   │   │   ├── BuildArchiveToolTask.cs  ← NEW
│   │   │   ├── CompileShadersTask.cs
│   │   │   ├── CompressModsTask.cs
│   │   │   ├── BuildTask.cs
│   │   │   ├── CopyModsTask.cs
│   │   │   ├── TestTask.cs
│   │   │   └── PublishTask.cs
│   │   └── MonoBall.Build.csproj
│   ├── build.ps1
│   └── build.sh
├── .github/
│   └── workflows/
│       └── build.yml                 ← NEW: GitHub Actions workflow
├── MonoBall.Core/
├── MonoBall.DesktopGL/
└── MonoBall.ArchiveTool/
```

### 8.3 Updated dotnet-tools.json

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "cake.frosting.tool": {
      "version": "4.0.0",
      "commands": ["dotnet-cake"]
    },
    "csharpier": {
      "version": "1.2.3",
      "commands": ["csharpier"]
    },
    "dotnet-mgcb": {
      "version": "3.8.5-preview.1",
      "commands": ["mgcb"]
    },
    "dotnet-mgfxc": {
      "version": "3.8.5-preview.1",
      "commands": ["mgfxc"]
    }
  }
}
```

---

## 9. Implementation Checklist

- [ ] Fix task dependency order
- [ ] Change build directory to `.build/`
- [ ] Update bootstrapping scripts to use local tools
- [ ] Complete BuildContext implementation
- [ ] Add BuildArchiveToolTask
- [ ] Add GitHub Actions workflow
- [ ] Add caching strategy
- [ ] Add platform detection for executables
- [ ] Add error handling to all tasks
- [ ] Add build validation
- [ ] Add incremental build support
- [ ] Test on Windows, Linux, macOS
- [ ] Test in GitHub Actions
- [ ] Update documentation

---

## 10. References

- [Cake Frosting GitHub Actions Integration](https://cakebuild.net/docs/integrations/build-systems/github-actions)
- [Cake Frosting Best Practices](https://cakebuild.net/docs/writing-builds/tasks)
- [MonoGame Build System](https://github.com/MonoGame/MonoGame/tree/develop/build)
