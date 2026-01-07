#!/usr/bin/env pwsh
<#
.SYNOPSIS
    MonoBall Build Script

.DESCRIPTION
    Bootstrapper for Cake Frosting build system.

.PARAMETER Target
    The build target to run. Default is 'Default' (full build + publish).
    Available targets:
    - Default        : Full build + publish (Release)
    - Build          : Build all projects
    - CompileShaders : Compile mod shaders only
    - CompressMods   : Compress mods to .monoball archives
    - CopyMods       : Copy mods to output directories
    - Clean          : Clean build artifacts
    - Restore        : Restore NuGet packages
    - Analyze        : Run code analysis
    - Test           : Run tests
    - Publish        : Publish the application

.PARAMETER Configuration
    Build configuration: Debug or Release. Default is Release.

.PARAMETER AllConfigurations
    When specified, copies mods to both Debug and Release output directories.

.PARAMETER SkipShaderCompilation
    Skip compiling shaders.

.PARAMETER SkipModCompression
    Skip compressing mods.

.PARAMETER SkipModCopy
    Skip copying mods to output.

.PARAMETER SkipTests
    Skip running tests.

.PARAMETER TreatWarningsAsErrors
    Treat analyzer warnings as errors.

.EXAMPLE
    ./build.ps1
    # Full Release build

.EXAMPLE
    ./build.ps1 -Configuration Debug
    # Full Debug build

.EXAMPLE
    ./build.ps1 -Target Build -Configuration Debug
    # Build only, Debug mode

.EXAMPLE
    ./build.ps1 -Target CopyMods -AllConfigurations
    # Copy mods to Debug + Release

.EXAMPLE
    ./build.ps1 -Target CompileShaders
    # Compile shaders only
#>

param(
    [string]$Target,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration,
    [switch]$AllConfigurations,
    [switch]$SkipShaderCompilation,
    [switch]$SkipModCompression,
    [switch]$SkipModCopy,
    [switch]$SkipTests,
    [switch]$TreatWarningsAsErrors
)

$ErrorActionPreference = "Stop"

# Restore dotnet tools
dotnet tool restore

# Build arguments for Cake
$cakeArgs = @()

if ($Target) {
    $cakeArgs += "--target=$Target"
}

if ($Configuration) {
    $cakeArgs += "--configuration=$Configuration"
}

if ($AllConfigurations) {
    $cakeArgs += "--all-configurations"
}

if ($SkipShaderCompilation) {
    $cakeArgs += "--skip-shader-compilation"
}

if ($SkipModCompression) {
    $cakeArgs += "--skip-mod-compression"
}

if ($SkipModCopy) {
    $cakeArgs += "--skip-mod-copy"
}

if ($SkipTests) {
    $cakeArgs += "--skip-tests"
}

if ($TreatWarningsAsErrors) {
    $cakeArgs += "--treat-warnings-as-errors"
}

# Run Cake Frosting build project directly
dotnet run --project .build/MonoBall.Build/MonoBall.Build.csproj -- @cakeArgs
