# MonoBall

A mod-based game engine built with MonoGame and .NET 10.0, featuring an Entity Component System (ECS) architecture.

## Building

MonoBall uses [Cake Frosting](https://cakebuild.net/docs/getting-started/setting-up-a-new-frosting-project) for its build system, providing a cross-platform, maintainable C#-based build automation.

### Prerequisites

- .NET 10.0 SDK (see `global.json` for version requirements)
- MonoGame 3.8.5-preview.1 or later

### Quick Start

**Windows:**

```powershell
./build.ps1
```

**Linux/macOS:**

```bash
./build.sh
```

### Build Arguments

The build system supports several command-line arguments:

**Configuration:**

- `--configuration=Release` (default) or `--configuration=Debug`
- `--target-framework=net10.0` (default)

**Task Control:**

- `--skip-tests` - Skip test execution
- `--skip-shader-compilation` - Skip shader compilation (faster iteration)
- `--skip-mod-compression` - Skip mod compression (faster iteration)
- `--skip-mod-copy` - Skip copying mods to output directory
- `--treat-warnings-as-errors` - Treat analyzer warnings as errors (fails build on warnings)

**Path Overrides (optional):**

- `--solution-path=<path>` - Override solution file path
- `--mods-directory=<path>` - Override Mods directory path
- `--output-directory=<path>` - Override output directory

### Examples

**Debug Build:**

```bash
./build.sh --configuration=Debug
```

**Skip Mod Operations (for faster iteration):**

```bash
./build.sh --skip-shader-compilation --skip-mod-compression
```

**Run Specific Task:**

```bash
./build.sh --target=Build
./build.sh --target=Clean
./build.sh --target=Publish
```

### Available Tasks

- `Clean` - Clean build artifacts and output directories
- `Restore` - Restore NuGet packages and dotnet tools
- `BuildArchiveTool` - Build ArchiveTool early (required for mod compression)
- `CompileShaders` - Compile mod shaders from .fx to .mgfxo
- `CompressMods` - Compress mod directories to .monoball archives
- `Build` - Build solution (Core + DesktopGL)
- `Analyze` - Run Roslynator analyzers to find code quality issues (can use `--treat-warnings-as-errors` flag)
- `GenerateApiDocs` - Generate .NET API documentation for MonoBall.Core using Roslynator (runs as part of Publish)
- `CopyMods` - Copy compiled mods to output directory
- `Publish` - Publish the application (includes API documentation generation)
- `Default` - Runs full build, analysis, and publish pipeline (default task)
- `Test` - Run unit tests (placeholder for future)

**Note on API Documentation Generation:**
The `GenerateApiDocs` task uses Roslynator's `generate-doc` command with `--ignored-names` to exclude Arch namespaces. Due to Roslynator's limitation (only accepts a single namespace prefix), additional external libraries (e.g., Serilog) are excluded via a post-processing step that removes their folders from the generated documentation. This is documented as a known limitation and may be improved in future Roslynator versions.

**Note on IDE Builds:**
When building from IDEs (Visual Studio, Rider, etc.), mods are automatically copied to the output directory after build via the `CopyModsToOutput` MSBuild target. For full mod operations (shader compilation, mod compression), use the Cake build system.

### Environment Variables

You can override paths using environment variables:

- `MONOBALL_MODS_DIR` - Override Mods directory path
- `MONOBALL_OUTPUT_DIR` - Override output directory
- `MONOBALL_CONFIGURATION` - Override build configuration

### CI/CD Integration

The project includes GitHub Actions workflows (`.github/workflows/build.yml`) that:

- Build on Windows, Linux, and macOS
- Cache NuGet packages and dotnet tools for faster builds
- Upload artifacts for distribution

### Migration from MSBuild

The build system has been migrated from MSBuild targets to Cake Frosting. All build logic previously in `MonoBall.DesktopGL.csproj` has been moved to the Cake Frosting build project in `.build/MonoBall.Build/`.

**Benefits:**

- Cross-platform compatibility (no PowerShell-specific commands)
- Full IDE support (IntelliSense, debugging)
- Better maintainability (C# instead of MSBuild XML)
- Easier to extend with new build tasks

### Troubleshooting

**Build fails with "Solution not found":**

- Ensure you're running the build script from the repository root
- Verify `MonoBall.slnx` (or `MonoBall.sln` for compatibility) exists in the root directory

**Shader compilation fails:**

- Ensure `dotnet-mgfxc` tool is installed: `dotnet tool restore`
- Check that shader files are valid MonoGame shaders
- **On macOS:** MGFXC requires Wine and a MonoGame Wine prefix to compile shaders:
  - Ensure Wine is installed and in PATH: `which wine` and `wine --version`
  - MGFXC requires a Wine prefix specifically set up for MonoGame (usually `~/.winemonogame`)
  - Check if Wine prefix exists: `ls -la ~/.winemonogame`
  - Check if `MGFXC_WINE_PATH` environment variable is set: `echo $MGFXC_WINE_PATH`
  - If Wine prefix doesn't exist or is misconfigured, run the MonoGame Wine setup script
  - The Wine prefix must have the .NET SDK installed (check `~/.winemonogame/drive_c/Program Files/dotnet`)
  - Visit https://docs.monogame.net/errors/mgfx0001?tab=macos for detailed troubleshooting and setup instructions
  - You can skip shader compilation with `--skip-shader-compilation` flag

**Mod compression fails:**

- Ensure ArchiveTool builds successfully (check `BuildArchiveTool` task)
- Verify mod directories don't contain invalid characters

**Tools not found:**

- Run `dotnet tool restore` manually
- Check `dotnet-tools.json` for correct tool versions

## Project Structure

```
MonoBall-v2/
├── build.ps1                    # Windows build script
├── build.sh                     # Unix build script
├── .build/                      # Build system
│   └── MonoBall.Build/          # Cake Frosting build project
├── MonoBall.Core/               # Core game library
├── MonoBall.DesktopGL/          # Desktop GL game executable
├── MonoBall.ArchiveTool/        # Mod archive utility
└── Mods/                        # Mod definitions and assets
```

## Documentation

- [Design Documentation](./docs/design/) - System design documents
- [Architecture Documentation](./docs/architecture/) - Architecture analysis
- [Guides](./docs/guides/) - Development guides

## License

[Add your license information here]
