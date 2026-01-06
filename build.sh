#!/usr/bin/env bash
set -euo pipefail

# Restore dotnet tools
dotnet tool restore

# Run Cake Frosting build project directly
dotnet run --project .build/MonoBall.Build/MonoBall.Build.csproj -- "$@"
