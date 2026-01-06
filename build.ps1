#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Bootstrapper for Cake Frosting build
#>

$ErrorActionPreference = "Stop"

# Restore dotnet tools
dotnet tool restore

# Run Cake Frosting build project directly
dotnet run --project .build/MonoBall.Build/MonoBall.Build.csproj -- $args
