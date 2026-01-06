#!/usr/bin/env bash
set -euo pipefail

# MonoBall Build Script
# Usage: ./build.sh [target] [options]
#
# Targets:
#   Default        - Full build + publish (Release)
#   Build          - Build all projects
#   CompileShaders - Compile mod shaders only
#   CompressMods   - Compress mods to .monoball archives
#   CopyMods       - Copy mods to output directories
#   Clean          - Clean build artifacts
#   Restore        - Restore NuGet packages
#   Analyze        - Run code analysis
#   Test           - Run tests
#   Publish        - Publish the application
#
# Options:
#   --configuration=<Debug|Release>  Build configuration (default: Release)
#   --all-configurations             Copy mods to both Debug and Release output dirs
#   --skip-shader-compilation        Skip compiling shaders
#   --skip-mod-compression           Skip compressing mods
#   --skip-mod-copy                  Skip copying mods to output
#   --skip-tests                     Skip running tests
#   --treat-warnings-as-errors       Treat analyzer warnings as errors
#
# Examples:
#   ./build.sh                                    # Full Release build
#   ./build.sh --configuration=Debug             # Full Debug build
#   ./build.sh Build --configuration=Debug       # Build only, Debug mode
#   ./build.sh CopyMods --all-configurations     # Copy mods to Debug + Release
#   ./build.sh CompileShaders                    # Compile shaders only

# Check for help flag
for arg in "$@"; do
    case "$arg" in
        -h|--help|help)
            echo "MonoBall Build Script"
            echo "Usage: ./build.sh [target] [options]"
            echo ""
            echo "Targets:"
            echo "  Default        - Full build + publish (Release)"
            echo "  Build          - Build all projects"
            echo "  CompileShaders - Compile mod shaders only"
            echo "  CompressMods   - Compress mods to .monoball archives"
            echo "  CopyMods       - Copy mods to output directories"
            echo "  Clean          - Clean build artifacts"
            echo "  Restore        - Restore NuGet packages"
            echo "  Analyze        - Run code analysis"
            echo "  Test           - Run tests"
            echo "  Publish        - Publish the application"
            echo ""
            echo "Options:"
            echo "  --configuration=<Debug|Release>  Build configuration (default: Release)"
            echo "  --all-configurations             Copy mods to both Debug and Release output dirs"
            echo "  --skip-shader-compilation        Skip compiling shaders"
            echo "  --skip-mod-compression           Skip compressing mods"
            echo "  --skip-mod-copy                  Skip copying mods to output"
            echo "  --skip-tests                     Skip running tests"
            echo "  --treat-warnings-as-errors       Treat analyzer warnings as errors"
            echo ""
            echo "Examples:"
            echo "  ./build.sh                                    # Full Release build"
            echo "  ./build.sh --configuration=Debug             # Full Debug build"
            echo "  ./build.sh Build --configuration=Debug       # Build only, Debug mode"
            echo "  ./build.sh CopyMods --all-configurations     # Copy mods to Debug + Release"
            echo "  ./build.sh CompileShaders                    # Compile shaders only"
            exit 0
            ;;
    esac
done

# Restore dotnet tools
dotnet tool restore

# Run Cake Frosting build project directly
dotnet run --project .build/MonoBall.Build/MonoBall.Build.csproj -- "$@"
