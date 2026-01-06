# Build Task Dependencies

This document describes the dependency chain for all build tasks in the MonoBall Cake Frosting build system.

## Task Dependency Graph

```
Clean (no dependencies)
  └─> Restore
       ├─> BuildCore (for GenerateApiDocs)
       │    └─> GenerateApiDocs
       ├─> BuildArchiveTool
       │    └─> CompressMods
       │         └─> Build
       │              ├─> CopyMods
       │              │    └─> Publish
       │              │         └─> Default
       │              └─> Test
       ├─> CompileShaders
       │    └─> CompressMods (see above)
       └─> Analyze (no dependencies on Build)
            └─> Default
```

## Task Descriptions and Dependencies

### Clean
- **Purpose**: Clean build artifacts and output directories
- **Dependencies**: None
- **When to use**: Explicitly or automatically before Restore

### Restore
- **Purpose**: Restore NuGet packages and dotnet tools
- **Dependencies**: Clean
- **When to use**: Required before any build/analysis operations

### BuildCore
- **Purpose**: Build only MonoBall.Core project (minimal build for documentation/analysis)
- **Dependencies**: Restore
- **When to use**:
  - API documentation generation
  - Quick builds for testing Core changes
- **Does NOT run**: ArchiveTool build, shader compilation, mod compression

### BuildArchiveTool
- **Purpose**: Build ArchiveTool early (required for mod compression)
- **Dependencies**: Restore
- **When to use**: Before mod compression operations

### CompileShaders
- **Purpose**: Compile mod shaders from .fx to .mgfxo
- **Dependencies**: Restore
- **When to use**: Before mod compression (shaders are included in mod archives)
- **Can skip**: Use `--skip-shader-compilation` flag

### CompressMods
- **Purpose**: Compress mod directories to .monoball archives
- **Dependencies**: BuildArchiveTool, CompileShaders
- **When to use**: Before full build (mods are copied to output)
- **Can skip**: Use `--skip-mod-compression` flag

### Build
- **Purpose**: Build solution (Core + DesktopGL)
- **Dependencies**: CompressMods
- **When to use**: Full build pipeline
- **Note**: ArchiveTool already built by CompressMods dependency

### CopyMods
- **Purpose**: Copy compiled mods to output directory
- **Dependencies**: Build
- **When to use**: Before publish
- **Can skip**: Use `--skip-mod-copy` flag

### Analyze
- **Purpose**: Run Roslynator analyzers to find code quality issues
- **Dependencies**: Restore (builds projects internally for analysis)
- **When to use**:
  - PR validation (with `--treat-warnings-as-errors`)
  - Code quality checks
- **Does NOT run**: Full build pipeline (only builds for analysis)

### GenerateApiDocs
- **Purpose**: Generate .NET API documentation using Roslynator
- **Dependencies**: BuildCore
- **When to use**:
  - Release publishing
  - Documentation updates
- **Does NOT run**: Full build, shader compilation, mod operations

### Publish
- **Purpose**: Publish the application
- **Dependencies**: GenerateApiDocs, CopyMods
- **When to use**: Release builds, deployment preparation

### Test
- **Purpose**: Run unit tests (placeholder for future)
- **Dependencies**: Build
- **When to use**: Testing phase
- **Can skip**: Use `--skip-tests` flag

### Default
- **Purpose**: Default task (runs full pipeline)
- **Dependencies**: Analyze, Publish
- **When to use**: Default action when no target specified

## Task Execution Scenarios

### Scenario 1: `--target=GenerateApiDocs`
**Runs**: Clean → Restore → BuildCore → GenerateApiDocs
**Skips**: ArchiveTool, Shaders, Mods, DesktopGL build

### Scenario 2: `--target=Analyze`
**Runs**: Clean → Restore → Analyze (builds projects internally)
**Skips**: Full build pipeline, shaders, mods

### Scenario 3: `--target=Build`
**Runs**: Clean → Restore → BuildArchiveTool → CompileShaders → CompressMods → Build
**Skips**: CopyMods, Publish, GenerateApiDocs

### Scenario 4: `--target=Publish`
**Runs**: Clean → Restore → BuildArchiveTool → CompileShaders → CompressMods → Build → CopyMods → BuildCore → GenerateApiDocs → Publish
**Note**: Full pipeline including documentation generation

### Scenario 5: `--target=Default` (or no target)
**Runs**: Clean → Restore → Analyze + (Full Publish pipeline)
**Note**: Runs both analysis and full publish

## Optimization Opportunities

1. **GenerateApiDocs**: Now uses BuildCore instead of Build (faster, no mod operations)
2. **Analyze**: Only builds projects for analysis, doesn't run full pipeline
3. **BuildCore**: Minimal build task for documentation/analysis scenarios

## Skip Flags

- `--skip-tests`: Skip test execution
- `--skip-shader-compilation`: Skip shader compilation
- `--skip-mod-compression`: Skip mod compression
- `--skip-mod-copy`: Skip copying mods to output
- `--treat-warnings-as-errors`: Treat analyzer warnings as errors (fail-fast)
