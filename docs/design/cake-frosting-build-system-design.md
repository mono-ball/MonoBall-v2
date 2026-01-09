# Cake Frosting Build System Design

## Executive Summary

This document outlines the design for migrating MonoBall's build system from MSBuild targets to Cake Frosting, following MonoGame's approach. Cake Frosting provides a C#-based build automation system with full IDE support, better maintainability, and cross-platform compatibility.

**Compliance:** This design fully complies with `.cursorrules` standards, including XML documentation, nullable types, fail-fast error handling, and SOLID principles.

---

## 1. Current Build System Analysis

### 1.1 Current Build Structure

**Solution Structure:**

- `MonoBall.sln` - Root solution file
- `MonoBall/MonoBall.sln` - Nested solution (more detailed)
- Projects:
  - `MonoBall.Core` - Core game library (net10.0)
  - `MonoBall.DesktopGL` - Desktop GL game executable (net10.0)
  - `MonoBall.ArchiveTool` - Mod archive utility (net10.0)

**Current Build Tasks (MSBuild Targets):**

1. **RestoreDotnetTools** (`BeforeTargets="CollectPackageReferences"`)

   - Restores dotnet tools via `dotnet tool restore`
   - Required for MonoGame tools (mgfxc, mgcb, etc.)

2. **CompileModShaders** (`BeforeTargets="CopyMods"`, depends on `RestoreDotnetTools`)

   - Discovers all `.fx` files in `Mods/**` directory
   - Compiles each `.fx` to `.mgfxo` using `dotnet tool run mgfxc`
   - Profile: OpenGL
   - Output: Same directory as source with `.mgfxo` extension

3. **CompressAllMods** (`BeforeTargets="CopyMods"`, depends on `CompileModShaders`)

   - Builds `MonoBall.ArchiveTool` if needed
   - Discovers all mod directories in `Mods/` folder
   - Compresses each directory to `.monoball` archive
   - Uses compression level 1
   - Command: `dotnet run --project ArchiveTool -- pack <dir> --output <archive> --compression-level 1`

4. **CopyMods** (`AfterTargets="Build"`)

   - Deletes old `Mods` directory in output
   - Copies `mod.manifest` file
   - Copies all `.monoball` archives to output `Mods/` directory

5. **CopyModsToPublish** (`AfterTargets="Publish"`)
   - Same as `CopyMods` but for publish directory

**Build Dependencies:**

```
RestoreDotnetTools
    ↓
CompileModShaders
    ↓
CompressAllMods
    ↓
CopyMods (after Build)
CopyModsToPublish (after Publish)
```

### 1.2 Current Tooling

**Dotnet Tools** (from `dotnet-tools.json`):

- `csharpier` (1.2.3) - Code formatter
- `dotnet-mgcb` (3.8.5-preview.1) - MonoGame Content Builder
- `dotnet-mgcb-editor-*` (3.8.5-preview.1) - MGCB Editor (platform-specific)
- `dotnet-mgfxc` (3.8.5-preview.1) - MonoGame Shader Compiler

**SDK Configuration** (`global.json`):

- SDK Version: 10.0.0
- Roll Forward: latestMajor
- Allow Prerelease: true

### 1.3 Current Build Issues & Limitations

**Issues:**

1. **Platform-Specific Scripts**: Uses PowerShell commands (`Get-ChildItem`) that don't work on Linux/macOS
2. **Complex MSBuild Targets**: Hard to maintain and debug
3. **No IDE Support**: MSBuild targets lack IntelliSense and debugging
4. **Tight Coupling**: Build logic embedded in project files
5. **Limited Reusability**: Can't easily reuse build logic across projects
6. **No Build Orchestration**: No easy way to run specific build scenarios

**Benefits of Migration:**

1. **Cross-Platform**: Cake Frosting works on Windows, Linux, macOS
2. **IDE Support**: Full C# IntelliSense, debugging, and refactoring
3. **Better Organization**: Separate build project, cleaner separation
4. **Maintainability**: Easier to read, test, and modify build logic
5. **Extensibility**: Easy to add new tasks and build scenarios
6. **CI/CD Integration**: Better integration with GitHub Actions, Azure DevOps, etc.

---

## 2. Cake Frosting Architecture Design

### 2.1 Project Structure

```
MonoBall/
├── .build/                           # Build system directory (hidden to avoid conflicts)
│   ├── MonoBall.Build/
│   │   ├── Program.cs                 # Cake host configuration
│   │   ├── BuildContext.cs           # Build context with properties
│   │   ├── Tasks/
│   │   │   ├── CleanTask.cs          # Clean output directories
│   │   │   ├── RestoreTask.cs        # Restore NuGet packages & tools
│   │   │   ├── BuildArchiveToolTask.cs # Build ArchiveTool early (CRITICAL)
│   │   │   ├── CompileShadersTask.cs # Compile mod shaders
│   │   │   ├── CompressModsTask.cs   # Compress mods to archives
│   │   │   ├── BuildTask.cs          # Build solution (Core + DesktopGL)
│   │   │   ├── TestTask.cs           # Run tests (future)
│   │   │   ├── CopyModsTask.cs       # Copy mods to output
│   │   │   └── PublishTask.cs        # Publish application
│   │   └── MonoBall.Build.csproj     # Build project file (with nullable enabled)
│   ├── build.ps1                     # Windows bootstrapper
│   └── build.sh                      # Unix bootstrapper
├── .github/
│   └── workflows/
│       └── build.yml                 # GitHub Actions workflow
├── MonoBall.Core/
├── MonoBall.DesktopGL/
└── MonoBall.ArchiveTool/
```

**Project File Configuration** (`.build/MonoBall.Build/MonoBall.Build.csproj`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>default</LangVersion>
    <RootNamespace>MonoBall.Build</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Cake.Frosting" Version="4.0.0" />
  </ItemGroup>
</Project>
```

**Key Configuration:**

- `<Nullable>enable</Nullable>` - Required for nullable reference types compliance
- `<RootNamespace>MonoBall.Build</RootNamespace>` - Matches folder structure

### 2.2 Build Context Design

**Namespace:** `MonoBall.Build`

**Compliance:** Follows `.cursorrules` standards (XML documentation, nullable types, validation, fail-fast error handling)

```csharp
namespace MonoBall.Build
{
    /// <summary>
    /// Build context containing configuration, paths, and build flags for Cake Frosting tasks.
    /// </summary>
    public class BuildContext : FrostingContext
    {
        private const string DefaultConfiguration = "Release";
        private const string DefaultTargetFramework = "net10.0";
        private const string DebugConfiguration = "Debug";
        private const string ReleaseConfiguration = "Release";

        /// <summary>
        /// Gets or sets the build configuration (Debug or Release).
        /// </summary>
        public string Configuration { get; set; }

        /// <summary>
        /// Gets or sets the target framework (e.g., "net10.0").
        /// </summary>
        public string TargetFramework { get; set; }

        /// <summary>
        /// Gets or sets the root directory of the solution.
        /// </summary>
        public DirectoryPath RootDirectory { get; set; }

        /// <summary>
        /// Gets or sets the path to the solution file.
        /// </summary>
        public FilePath SolutionPath { get; set; }

        /// <summary>
        /// Gets or sets the directory containing mods.
        /// </summary>
        public DirectoryPath ModsDirectory { get; set; }

        /// <summary>
        /// Gets or sets the output directory for build artifacts.
        /// </summary>
        public DirectoryPath OutputDirectory { get; set; }

        /// <summary>
        /// Gets or sets the publish directory for published artifacts, or null if not set.
        /// </summary>
        public DirectoryPath? PublishDirectory { get; set; }

        /// <summary>
        /// Gets or sets the path to the Core project file.
        /// </summary>
        public FilePath CoreProjectPath { get; set; }

        /// <summary>
        /// Gets or sets the path to the DesktopGL project file.
        /// </summary>
        public FilePath DesktopGLProjectPath { get; set; }

        /// <summary>
        /// Gets or sets the path to the ArchiveTool project file.
        /// </summary>
        public FilePath ArchiveToolProjectPath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip tests.
        /// </summary>
        public bool SkipTests { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip shader compilation.
        /// </summary>
        public bool SkipShaderCompilation { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip mod compression.
        /// </summary>
        public bool SkipModCompression { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to skip mod copying.
        /// </summary>
        public bool SkipModCopy { get; set; }

        /// <summary>
        /// Gets or sets the path to the MGFXC shader compiler tool, or null if not set.
        /// </summary>
        public FilePath? MgfxcPath { get; set; }

        /// <summary>
        /// Gets or sets the path to the ArchiveTool executable.
        /// </summary>
        public FilePath ArchiveToolExecutable { get; set; }

        /// <summary>
        /// Gets a value indicating whether the build is running on GitHub Actions.
        /// </summary>
        public bool IsRunningOnGitHubActions => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

        /// <summary>
        /// Gets the GitHub commit SHA, or null if not running on GitHub Actions.
        /// </summary>
        public string? GitHubSha => Environment.GetEnvironmentVariable("GITHUB_SHA");

        /// <summary>
        /// Gets a value indicating whether this is a pull request build.
        /// </summary>
        public bool IsPullRequest => Environment.GetEnvironmentVariable("GITHUB_REF")?.StartsWith("refs/pull/") ?? false;

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildContext"/> class.
        /// </summary>
        /// <param name="context">The Cake context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when solution file is not found.</exception>
        /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
        public BuildContext(ICakeContext context)
            : base(context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            // Build Configuration
            Configuration = context.Argument("configuration", DefaultConfiguration);
            if (Configuration != DebugConfiguration && Configuration != ReleaseConfiguration)
            {
                throw new ArgumentException(
                    $"Invalid configuration '{Configuration}'. Must be '{DebugConfiguration}' or '{ReleaseConfiguration}'.",
                    nameof(Configuration));
            }

            TargetFramework = context.Argument("target-framework", DefaultTargetFramework);

            // Path Resolution (relative to build project location)
            var buildProjectDir = context.MakeAbsolute(context.Directory("./.build/MonoBall.Build"));
            RootDirectory = buildProjectDir.Combine("../..").Collapse();
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

            // ArchiveTool Executable (platform-specific)
            var archiveToolBinDir = RootDirectory.Combine("MonoBall/MonoBall.ArchiveTool/bin")
                                                   .Combine(Configuration)
                                                   .Combine(TargetFramework);
            ArchiveToolExecutable = context.IsRunningOnWindows()
                ? archiveToolBinDir.CombineWithFilePath("MonoBall.ArchiveTool.exe")
                : archiveToolBinDir.CombineWithFilePath("MonoBall.ArchiveTool");

            // Build Flags
            SkipTests = context.HasArgument("skip-tests");
            SkipShaderCompilation = context.HasArgument("skip-shader-compilation");
            SkipModCompression = context.HasArgument("skip-mod-compression");
            SkipModCopy = context.HasArgument("skip-mod-copy");

            // Validate critical paths (fail fast)
            if (!context.FileExists(SolutionPath))
                throw new FileNotFoundException($"Solution not found: {SolutionPath}", SolutionPath.FullPath);

            if (!context.DirectoryExists(ModsDirectory))
                context.Log.Warning($"Mods directory not found: {ModsDirectory}");
        }
    }
}
```

### 2.3 Task Dependencies

**CRITICAL:** ArchiveTool must be built before CompressModsTask can use it.

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
BuildArchiveTool  ← CRITICAL: Build ArchiveTool early
    ↓
CompileShaders
    ↓
Restore
    ↓
Clean
```

---

## 3. Task Implementation Design

### 3.1 CleanTask

**Purpose:** Clean build artifacts and output directories

**Namespace:** `MonoBall.Build.Tasks`

**Error Handling:** Fail fast - throws exceptions if critical operations fail

**Implementation:**

```csharp
namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that cleans build artifacts and output directories.
    /// </summary>
    [TaskName("Clean")]
    public sealed class CleanTask : FrostingTask<BuildContext>
    {
        /// <summary>
        /// Runs the clean task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            context.Log.Information("Cleaning build artifacts...");

            // Clean bin/ and obj/ directories in all projects
            var projects = new[] { context.CoreProjectPath, context.DesktopGLProjectPath, context.ArchiveToolProjectPath };
            foreach (var project in projects)
            {
                var projectDir = project.GetDirectory();
                var binDir = projectDir.Combine("bin");
                var objDir = projectDir.Combine("obj");

                if (context.DirectoryExists(binDir))
                {
                    context.CleanDirectory(binDir);
                }

                if (context.DirectoryExists(objDir))
                {
                    context.CleanDirectory(objDir);
                }
            }

            // Clean output Mods directory if it exists
            var modsOutputDir = context.OutputDirectory.Combine("Mods");
            if (context.DirectoryExists(modsOutputDir))
            {
                context.CleanDirectory(modsOutputDir);
            }

            // Clean publish directory if it exists
            if (context.PublishDirectory != null && context.DirectoryExists(context.PublishDirectory))
            {
                context.CleanDirectory(context.PublishDirectory);
            }

            context.Log.Information("Clean completed successfully.");
        }
    }
}
```

**Dependencies:** None (first task)

### 3.2 RestoreTask

**Purpose:** Restore NuGet packages and dotnet tools

**Implementation:**

- Run `dotnet restore` on solution
- Run `dotnet tool restore` to restore dotnet tools
- Verify required tools are available (mgfxc, mgcb)

**Dependencies:** CleanTask

### 3.3 CompileShadersTask

**Purpose:** Compile mod shaders from `.fx` to `.mgfxo`

**Namespace:** `MonoBall.Build.Tasks`

**Error Handling:** Fail fast - throws exceptions if shader compilation fails (no graceful degradation)

**Implementation:**

```csharp
namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that compiles mod shaders from .fx to .mgfxo files.
    /// </summary>
    [TaskName("CompileShaders")]
    [IsDependentOn(typeof(RestoreTask))]
    public sealed class CompileShadersTask : FrostingTask<BuildContext>
    {
        private const string ShaderExtension = ".fx";
        private const string CompiledShaderExtension = ".mgfxo";
        private const string ShaderProfile = "OpenGL";

        /// <summary>
        /// Runs the shader compilation task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when shader compilation fails.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.SkipShaderCompilation)
            {
                context.Log.Information("Skipping shader compilation (--skip-shader-compilation flag set).");
                return;
            }

            if (!context.DirectoryExists(context.ModsDirectory))
            {
                context.Log.Warning($"Mods directory not found: {context.ModsDirectory}. Skipping shader compilation.");
                return;
            }

            context.Log.Information("Compiling mod shaders...");

            // Discover all .fx files in Mods directory
            var shaderFiles = context.GetFiles($"{context.ModsDirectory}/**/*{ShaderExtension}");
            var failedShaders = new List<string>();

            foreach (var shaderFile in shaderFiles)
            {
                try
                {
                    var outputPath = shaderFile.ChangeExtension(CompiledShaderExtension);
                    CompileShader(context, shaderFile, outputPath);
                }
                catch (Exception ex)
                {
                    context.Log.Error($"Failed to compile shader {shaderFile}: {ex.Message}");
                    failedShaders.Add(shaderFile.FullPath);
                }
            }

            if (failedShaders.Any())
            {
                throw new InvalidOperationException(
                    $"Failed to compile {failedShaders.Count} shader(s). " +
                    $"Failed shaders: {string.Join(", ", failedShaders)}");
            }

            context.Log.Information($"Shader compilation complete. Compiled {shaderFiles.Count} shader(s).");
        }

        private static void CompileShader(BuildContext context, FilePath inputPath, FilePath outputPath)
        {
            context.Log.Debug($"Compiling {inputPath} -> {outputPath}");

            var result = context.StartProcess(
                "dotnet",
                new ProcessSettings
                {
                    Arguments = new ProcessArgumentBuilder()
                        .Append("tool")
                        .Append("run")
                        .Append("mgfxc")
                        .Append("--")
                        .AppendQuoted(inputPath.FullPath)
                        .AppendQuoted(outputPath.FullPath)
                        .Append($"/Profile:{ShaderProfile}")
                });

            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"Shader compilation failed for {inputPath} with exit code {result}.");
            }
        }
    }
}
```

**Dependencies:** RestoreTask

**Cross-Platform Considerations:**

- Use Cake's `Globber` for file discovery (cross-platform)
- Use Cake's `ProcessRunner` for executing tools
- Fail fast with clear error messages (no fallback behavior)

### 3.4 BuildArchiveToolTask

**Purpose:** Build ArchiveTool early so it can be used by CompressModsTask

**Implementation:**

- Build only `MonoBall.ArchiveTool` project
- Verify executable exists after build
- Store executable path in BuildContext for use by CompressModsTask

**Dependencies:** RestoreTask

**Cross-Platform Considerations:**

- Use platform-specific executable name (`.exe` on Windows)
- Verify executable exists before proceeding

### 3.5 CompressModsTask

**Purpose:** Compress mod directories to `.monoball` archives

**Namespace:** `MonoBall.Build.Tasks`

**Error Handling:** Fail fast - throws exception if compression fails (no graceful degradation)

**Implementation:**

```csharp
namespace MonoBall.Build.Tasks
{
    /// <summary>
    /// Task that compresses mod directories to .monoball archives.
    /// </summary>
    [TaskName("CompressMods")]
    [IsDependentOn(typeof(BuildArchiveToolTask))]
    public sealed class CompressModsTask : FrostingTask<BuildContext>
    {
        private const string ArchiveExtension = ".monoball";
        private const int CompressionLevel = 1;

        /// <summary>
        /// Runs the mod compression task.
        /// </summary>
        /// <param name="context">The build context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when ArchiveTool executable is not found.</exception>
        /// <exception cref="InvalidOperationException">Thrown when mod compression fails.</exception>
        public override void Run(BuildContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (context.SkipModCompression)
            {
                context.Log.Information("Skipping mod compression (--skip-mod-compression flag set).");
                return;
            }

            if (!context.FileExists(context.ArchiveToolExecutable))
            {
                throw new FileNotFoundException(
                    $"ArchiveTool executable not found: {context.ArchiveToolExecutable}. " +
                    "Ensure BuildArchiveToolTask completed successfully.",
                    context.ArchiveToolExecutable.FullPath);
            }

            if (!context.DirectoryExists(context.ModsDirectory))
            {
                context.Log.Warning($"Mods directory not found: {context.ModsDirectory}. Skipping mod compression.");
                return;
            }

            context.Log.Information("Compressing mods...");

            // Discover all directories in Mods folder (excluding .monoball files)
            var modDirectories = context.GetDirectories($"{context.ModsDirectory}/*")
                .Where(dir => !dir.GetDirectoryName().EndsWith(ArchiveExtension))
                .ToList();

            var failedMods = new List<string>();

            foreach (var modDir in modDirectories)
            {
                try
                {
                    var archiveName = $"{modDir.GetDirectoryName()}{ArchiveExtension}";
                    var archivePath = context.ModsDirectory.CombineWithFilePath(archiveName);
                    CompressMod(context, modDir, archivePath);
                }
                catch (Exception ex)
                {
                    context.Log.Error($"Failed to compress mod {modDir}: {ex.Message}");
                    failedMods.Add(modDir.FullPath);
                }
            }

            if (failedMods.Any())
            {
                throw new InvalidOperationException(
                    $"Failed to compress {failedMods.Count} mod(s). " +
                    $"Failed mods: {string.Join(", ", failedMods)}");
            }

            context.Log.Information($"Mod compression complete. Compressed {modDirectories.Count} mod(s).");
        }

        private static void CompressMod(BuildContext context, DirectoryPath modDir, FilePath archivePath)
        {
            context.Log.Debug($"Compressing {modDir} -> {archivePath}");

            var result = context.StartProcess(
                context.ArchiveToolExecutable,
                new ProcessSettings
                {
                    Arguments = new ProcessArgumentBuilder()
                        .Append("pack")
                        .AppendQuoted(modDir.FullPath)
                        .Append("--output")
                        .AppendQuoted(archivePath.FullPath)
                        .Append("--compression-level")
                        .Append(CompressionLevel.ToString())
                });

            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"Mod compression failed for {modDir} with exit code {result}.");
            }
        }
    }
}
```

**Dependencies:** BuildArchiveToolTask

**Cross-Platform Considerations:**

- Use Cake's `GetDirectories()` for directory discovery
- Use platform-specific executable from BuildContext
- Handle path separators correctly
- Fail fast with clear error messages (no fallback behavior)

### 3.6 BuildTask

**Purpose:** Build the solution (Core + DesktopGL only, ArchiveTool already built)

**Implementation:**

- Build `MonoBall.Core` project
- Build `MonoBall.DesktopGL` project
- Skip `MonoBall.ArchiveTool` (already built by BuildArchiveToolTask)
- Handle build errors with clear messages

**Dependencies:** CompressModsTask

### 3.7 CopyModsTask

**Purpose:** Copy mods to output directory

**Implementation:**

- Delete existing `Mods/` directory in output if it exists
- Copy `mod.manifest` file
- Copy all `.monoball` archives
- Skip if `SkipModCopy` flag is set

**Dependencies:** BuildTask

### 3.8 TestTask (Future)

**Purpose:** Run unit tests

**Implementation:**

- Discover test projects
- Run `dotnet test` with appropriate filters
- Generate test reports

**Dependencies:** BuildTask

### 3.9 PublishTask

**Purpose:** Publish the application

**Implementation:**

- Run `dotnet publish` on DesktopGL project
- Copy mods to publish directory (similar to CopyModsTask)
- Handle platform-specific publish settings

**Dependencies:** CopyModsTask

---

## 4. Build Arguments & Configuration

### 4.1 Command-Line Arguments

```bash
# Configuration
--configuration=Release|Debug          # Build configuration (default: Release)
--target-framework=net10.0             # Target framework (default: net10.0)

# Task Control
--skip-tests                           # Skip test execution
--skip-shader-compilation              # Skip shader compilation
--skip-mod-compression                 # Skip mod compression
--skip-mod-copy                        # Skip mod copying

# Paths (optional, auto-detected)
--solution-path=<path>                 # Path to solution file
--mods-directory=<path>                # Path to Mods directory
--output-directory=<path>              # Output directory
```

### 4.2 Environment Variables

- `MONOBALL_MODS_DIR` - Override Mods directory path
- `MONOBALL_OUTPUT_DIR` - Override output directory
- `MONOBALL_CONFIGURATION` - Override build configuration

### 4.3 Default Values

- Configuration: `Release`
- Target Framework: `net10.0`
- Solution Path: Auto-detected from build project location
- Mods Directory: `../../Mods` relative to build project
- Output Directory: `bin/<Configuration>/<TargetFramework>/` relative to DesktopGL project

---

## 5. Bootstrapping Scripts

### 5.1 build.ps1 (Windows)

```powershell
#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Bootstrapper for Cake Frosting build
#>

$ErrorActionPreference = "Stop"

# Restore dotnet tools (including Cake Frosting from dotnet-tools.json)
dotnet tool restore

# Run build using local Cake Frosting tool
dotnet cake .build/MonoBall.Build/MonoBall.Build.csproj -- $args
```

### 5.2 build.sh (Unix)

```bash
#!/usr/bin/env bash
set -euo pipefail

# Restore dotnet tools (including Cake Frosting from dotnet-tools.json)
dotnet tool restore

# Run build using local Cake Frosting tool
dotnet cake .build/MonoBall.Build/MonoBall.Build.csproj -- "$@"
```

---

## 6. Migration Plan

### 6.1 Phase 1: Setup Cake Frosting Project

1. Install Cake Frosting template:

   ```bash
   dotnet new install Cake.Frosting.Template
   ```

2. Create build project:

   ```bash
   dotnet new cakefrosting -n MonoBall.Build -o .build/MonoBall.Build
   ```

3. Add build project to solution:

   ```bash
   dotnet sln MonoBall.sln add .build/MonoBall.Build/MonoBall.Build.csproj
   ```

4. Update `dotnet-tools.json` to include Cake Frosting:

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

5. Create bootstrapping scripts (`build.ps1`, `build.sh`)

### 6.2 Phase 2: Implement Core Tasks

1. **Configure Project:**

   - Add `<Nullable>enable</Nullable>` to `.build/MonoBall.Build/MonoBall.Build.csproj`
   - Ensure namespace structure matches folder structure

2. **Implement `BuildContext`:**

   - Complete initialization with validation (see Section 2.2)
   - Add XML documentation to all public members
   - Add null checks and argument validation
   - Use constants for magic strings

3. **Implement Tasks (with XML documentation):**

   - Implement `CleanTask` (see Section 3.1)
   - Implement `RestoreTask`
   - Implement `BuildArchiveToolTask` (CRITICAL: must come before CompressModsTask)
   - Implement `BuildTask` (builds Core + DesktopGL only)

4. **Test basic build flow:**
   - Verify all tasks execute in correct order
   - Verify error handling (fail fast)
   - Verify XML documentation generates correctly

### 6.3 Phase 3: Implement Mod Build Tasks

1. Implement `CompileShadersTask`
2. Implement `CompressModsTask`
3. Implement `CopyModsTask`
4. Test mod build flow

### 6.4 Phase 4: Implement Publish Task

1. Implement `PublishTask`
2. Test publish flow
3. Verify mods are copied to publish directory

### 6.5 Phase 5: Remove MSBuild Targets

**Critical:** This phase removes ~195 lines of MSBuild XML from the project file.

#### 5.1 Targets to Remove from `MonoBall.DesktopGL.csproj`

Remove the following MSBuild `<Target>` elements (all replaced by Cake Frosting tasks):

1. **`RestoreDotnetTools`** (lines ~40-46)

   - Replaced by: `RestoreTask` in Cake Frosting
   - Removes: `dotnet tool restore` execution

2. **`CompileModShaders`** (lines ~117-145)

   - Replaced by: `CompileShadersTask` in Cake Frosting
   - Removes: Shader discovery and compilation logic (~29 lines)
   - Removes: PowerShell/Windows-specific path handling

3. **`CompressAllMods`** (lines ~53-110)

   - Replaced by: `CompressModsTask` + `BuildArchiveToolTask` in Cake Frosting
   - Removes: PowerShell `Get-ChildItem` command (~58 lines)
   - Removes: Temporary file handling (`moddirs.txt`)
   - Removes: ArchiveTool build logic

4. **`CopyMods`** (lines ~154-190)

   - Replaced by: `CopyModsTask` in Cake Frosting
   - Removes: Mod copying logic (~37 lines)

5. **`CopyModsToPublish`** (lines ~198-234)
   - Replaced by: `PublishTask` (includes mod copy) in Cake Frosting
   - Removes: Publish-specific mod copying logic (~37 lines)

#### 5.2 Before/After Comparison

**Before (Current):** ~236 lines with 5 MSBuild targets
**After (Cleaned):** ~41 lines, no MSBuild targets

**Removed Content:**

- `RestoreDotnetTools` target (~7 lines)
- `CompileModShaders` target (~29 lines)
- `CompressAllMods` target (~58 lines) - includes PowerShell commands
- `CopyMods` target (~37 lines)
- `CopyModsToPublish` target (~37 lines)
- All comments related to build targets (~27 lines)

**Total Reduction:** ~195 lines removed (~83% reduction in project file size)

#### 5.3 Cleaned Project File Structure

After cleanup, `MonoBall.DesktopGL.csproj` should contain only:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishReadyToRun>false</PublishReadyToRun>
    <TieredCompilation>false</TieredCompilation>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Icon.ico</ApplicationIcon>
    <AssemblyName>MonoBall</AssemblyName>
    <LangVersion>default</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="Icon.ico">
      <LogicalName>Icon.ico</LogicalName>
    </EmbeddedResource>
    <EmbeddedResource Include="Icon.bmp">
      <LogicalName>Icon.bmp</LogicalName>
    </EmbeddedResource>
  </ItemGroup>

  <ItemGroup>
    <MonoGameContentReference Include="..\MonoBall.Core\Content\MonoBall.mgcb">
      <Link>Content\MonoBall.mgcb</Link>
    </MonoGameContentReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MonoBall.Core\MonoBall.Core.csproj" />
    <!-- ArchiveTool reference can be removed if not needed for IDE IntelliSense -->
    <!-- <ProjectReference Include="..\MonoBall.ArchiveTool\MonoBall.ArchiveTool.csproj">
      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
      <SkipGetTargetFrameworkProperties>true</SkipGetTargetFrameworkProperties>
    </ProjectReference> -->
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MonoGame.Framework.DesktopGL" Version="3.8.5-preview.1" />
    <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.5-preview.1" />
    <PackageReference Include="CSharpier.MSBuild" Version="1.2.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

**Removed:**

- All 5 `<Target>` elements (~195 lines)
- PowerShell-specific commands
- Platform-specific path handling
- Build orchestration logic

**Kept:**

- Project metadata and properties
- Package references
- Content references
- Embedded resources

#### 5.4 Verification Steps

1. **Remove targets incrementally** (one at a time) to verify each works:

   - First remove `RestoreDotnetTools` → Test build
   - Then remove `CompileModShaders` → Test build
   - Then remove `CompressAllMods` → Test build
   - Then remove `CopyMods` → Test build
   - Finally remove `CopyModsToPublish` → Test publish

2. **Verify Cake Frosting handles all functionality:**

   - ✅ Tools are restored
   - ✅ Shaders are compiled
   - ✅ Mods are compressed
   - ✅ Mods are copied to output
   - ✅ Mods are copied to publish directory

3. **Test on all platforms:**

   - Windows (Visual Studio)
   - Linux (CLI)
   - macOS (CLI)

4. **Update documentation:**
   - Remove references to MSBuild targets
   - Update build instructions to use Cake Frosting
   - Update CI/CD documentation

#### 5.5 Optional: Remove ArchiveTool Project Reference

The `ArchiveTool` project reference in `MonoBall.DesktopGL.csproj` was only needed for MSBuild to build it. With Cake Frosting handling the build separately, this reference can be removed if:

- IDE IntelliSense doesn't need it
- No runtime dependency exists

**Decision:** Keep it commented out initially, remove if not needed after testing.

### 6.6 Phase 6: CI/CD Integration

1. Add GitHub Actions workflow (`.github/workflows/build.yml`)
2. Configure build for CI environment
3. Add caching for NuGet packages and dotnet tools
4. Add artifact upload/download
5. Add test task integration
6. Test on multiple platforms (Windows, Linux, macOS)

---

## 7. Example Usage

### 7.1 Basic Build

```bash
# Windows
./build.ps1

# Unix
./build.sh
```

### 7.2 Debug Build

```bash
./build.sh --configuration=Debug
```

### 7.3 Skip Mod Compression (for faster iteration)

```bash
./build.sh --skip-mod-compression --skip-shader-compilation
```

### 7.4 Build and Publish

```bash
./build.sh --target=Publish
```

### 7.5 Clean Build

```bash
./build.sh --target=Clean
```

---

## 8. Benefits Summary

1. **Cross-Platform**: Works on Windows, Linux, macOS without platform-specific scripts
2. **IDE Support**: Full C# IntelliSense, debugging, and refactoring in Visual Studio/Rider
3. **Maintainability**: Clean, testable C# code instead of MSBuild XML
4. **Extensibility**: Easy to add new tasks (formatting, linting, packaging, etc.)
5. **CI/CD Ready**: Better integration with GitHub Actions, Azure DevOps
6. **Better Error Messages**: C# exceptions with stack traces instead of MSBuild errors
7. **Testability**: Can unit test build logic if needed
8. **Documentation**: Self-documenting C# code with XML comments

---

## 9. Future Enhancements

1. **Test Task**: Add unit test execution
2. **Code Formatting**: Add CSharpier formatting task
3. **Linting**: Add code analysis/linting task
4. **Packaging**: Add NuGet package creation task
5. **Versioning**: Add version management task
6. **Release Notes**: Add changelog generation task
7. **Docker**: Add Docker image build task
8. **Multi-Platform Builds**: Add tasks for building on different platforms

---

## 10. GitHub Actions Integration

### 10.1 Basic Workflow

See `.github/workflows/build.yml` for complete workflow example. Key features:

- Multi-platform builds (Windows, Linux, macOS)
- Caching for NuGet packages and dotnet tools
- Artifact upload for distribution
- Pull request and push triggers

### 10.2 Caching Strategy

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

---

## 11. .cursorrules Compliance

### 11.1 Compliance Status

The design follows all `.cursorrules` standards:

- ✅ **No Backward Compatibility**: Removes all MSBuild targets, no compatibility layers
- ✅ **No Fallback Code**: Tasks fail fast with clear exceptions, no silent degradation
- ✅ **Nullable Types**: Properly marked nullable properties, returns `null` instead of empty strings
- ✅ **Dependency Injection**: Required dependencies in constructor with null checks
- ✅ **XML Documentation**: All public APIs documented with XML comments
- ✅ **Namespace Structure**: `MonoBall.Build` and `MonoBall.Build.Tasks` match folder structure
- ✅ **File Organization**: One class per file, PascalCase naming
- ✅ **Exception Handling**: Validates arguments, throws appropriate exceptions with parameter names
- ✅ **SOLID Principles**: Single responsibility, dependency injection, open/closed
- ✅ **DRY**: Common logic extracted to helper methods, constants for magic strings

### 11.2 Key Compliance Features

**Error Handling:**

- All tasks fail fast with clear exceptions
- No graceful degradation or fallback behavior
- Exceptions include parameter names and context

**Nullable Types:**

- Properties marked with `?` where nullable
- Returns `null` instead of empty strings for optional values
- Requires `<Nullable>enable</Nullable>` in `.build/MonoBall.Build/MonoBall.Build.csproj`

**XML Documentation:**

- All public classes, properties, and methods documented
- Includes `<summary>`, `<param>`, `<returns>`, `<exception>` tags
- Documents when exceptions are thrown

**Validation:**

- Constructor validates all required dependencies
- Throws `ArgumentNullException` for null arguments
- Throws `ArgumentException` for invalid values
- Throws `FileNotFoundException` for missing files

See `CAKE_FROSTING_BUILD_SYSTEM_DESIGN_CURSORRULES_ANALYSIS.md` for detailed compliance analysis.

---

## 12. Known Issues & Solutions

See `CAKE_FROSTING_BUILD_SYSTEM_DESIGN_ANALYSIS.md` for detailed analysis of:

- Architecture issues and fixes
- GitHub Actions integration requirements
- Cross-platform compatibility concerns
- Error handling strategies
- Performance optimizations

---

## 13. References

- [Cake Frosting Documentation](https://cakebuild.net/docs/getting-started/setting-up-a-new-frosting-project)
- [Cake Frosting GitHub Actions Integration](https://cakebuild.net/docs/integrations/build-systems/github-actions)
- [MonoGame PR 8225](https://github.com/MonoGame/MonoGame/pull/8225) - MonoGame's Cake Frosting migration
- [Cake Frosting Examples](https://github.com/cake-build/cake/tree/develop/src/Cake.Frosting.Template)

---

## Appendix A: Task Dependency Graph

```
Default
├── Publish
│   ├── CopyMods
│   │   ├── Build (Core + DesktopGL)
│   │   │   ├── CompressMods
│   │   │   │   ├── BuildArchiveTool  ← CRITICAL: Must come before CompressMods
│   │   │   │   │   └── Restore
│   │   │   │   │       └── Clean
│   │   │   │   └── CompileShaders
│   │   │   │       └── Restore
│   │   │   └── Restore
│   │   └── Restore
│   └── Restore
└── Clean
```

## Appendix B: MSBuild Target to Cake Task Mapping

| MSBuild Target       | Cake Task                         | Lines Removed | Notes                                 |
| -------------------- | --------------------------------- | ------------- | ------------------------------------- |
| `RestoreDotnetTools` | `RestoreTask`                     | ~7            | Also restores NuGet packages          |
| `CompileModShaders`  | `CompileShadersTask`              | ~29           | Same logic, cross-platform            |
| `CompressAllMods`    | `CompressModsTask`                | ~58           | Requires `BuildArchiveToolTask` first |
| _(implicit)_         | `BuildArchiveToolTask`            | N/A           | NEW: Builds ArchiveTool early         |
| `CopyMods`           | `CopyModsTask`                    | ~37           | Same logic                            |
| `CopyModsToPublish`  | `PublishTask` (includes mod copy) | ~37           | Integrated into publish               |
| **Total Removed**    |                                   | **~195**      | All build orchestration moved to Cake |

**Cleanup Impact:**

- Removes ~195 lines of MSBuild XML from `MonoBall.DesktopGL.csproj`
- Removes all PowerShell-specific commands
- Removes platform-specific path handling
- Simplifies project file to only contain project metadata

## Appendix C: Critical Fixes Applied

1. **Task Dependency Order**: Added `BuildArchiveToolTask` before `CompressModsTask`
2. **Build Directory**: Changed from `build/` to `.build/` to avoid conflicts
3. **Tool Installation**: Changed from global to local tool installation via `dotnet-tools.json`
4. **BuildContext**: Completed initialization implementation with path resolution
5. **Platform Detection**: Added platform-specific executable handling
6. **GitHub Actions**: Added workflow example and caching strategy
7. **MSBuild Cleanup**: Detailed plan to remove ~195 lines of MSBuild targets from `.csproj` files
8. **.cursorrules Compliance**: Added XML documentation, nullable types, validation, fail-fast error handling
9. **Namespace Structure**: Specified `MonoBall.Build` and `MonoBall.Build.Tasks` namespaces
10. **Constants**: Added constants for magic strings (configuration values, extensions, etc.)

See `CAKE_FROSTING_BUILD_SYSTEM_DESIGN_ANALYSIS.md` for detailed architecture analysis.
See `CAKE_FROSTING_BUILD_SYSTEM_DESIGN_CURSORRULES_ANALYSIS.md` for detailed compliance analysis.
