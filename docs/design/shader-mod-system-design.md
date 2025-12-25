# Shader Mod System Design

> **Note**: See [shader-mod-system-design-analysis.md](./shader-mod-system-design-analysis.md) for architecture analysis, .cursorrules compliance review, and alignment with current mod system.

## Overview

This design enables mods to define custom shaders that are compiled during build using MGFXC and loaded at runtime without relying on the MonoGame Content Pipeline. Shaders are compiled to `.mgfxo` files (OpenGL profile only) and loaded directly using MonoGame's `Effect` constructor that accepts byte arrays.

## Goals

1. **Mod-Based Shader Definitions**: Shaders defined in mods via JSON definitions
2. **Build-Time Compilation**: Shaders compiled to `.mgfxo` during MSBuild
3. **Runtime Loading**: Load compiled shaders using `Effect(GraphicsDevice, byte[])` constructor
4. **OpenGL Only**: Target OpenGL profile exclusively
5. **No Content Pipeline**: Avoid dependency on MonoGame Content Pipeline for shaders
6. **Integration**: Use existing `DefinitionRegistry` pattern for shader definitions

## Architecture

### Components

1. **ShaderDefinition** - JSON definition structure for shaders (layer-agnostic)
2. **ShaderCompiler** - MSBuild task/target for compiling `.fx` → `.mgfxo`
3. **ShaderLoader** - Runtime loader for compiled `.mgfxo` files
4. **ShaderService Extension** - Extend `IShaderService` to support mod shaders

### Design Principles

- **Shader Reusability**: Shaders are not tied to specific layer types. A single shader can be applied to tile layers, sprite layers, combined layers, per-entity, or as screen-space effects.
- **Usage Context**: The rendering systems that apply shaders determine where they're used, not the shader definition itself.
- **Separation of Concerns**: Shader definitions describe what the shader IS (source file, parameters), not WHERE it's used.

### File Structure

```
Mods/
├── {mod-id}/
│   ├── mod.json
│   ├── Definitions/
│   │   └── Shaders/
│   │       └── {shader-id}.json          # Shader definition
│   └── Shaders/
│       └── {shader-id}.fx                # HLSL shader source
│
# After build:
Mods/
├── {mod-id}/
│   └── Shaders/
│       └── {shader-id}.mgfxo             # Compiled shader (OpenGL)
```

## Shader Definition Format

### JSON Structure

```json
{
  "id": "base:shader:ColorGrading",
  "name": "Color Grading Shader",
  "description": "Applies color grading effects (saturation, contrast, etc.)",
  "sourceFile": "Shaders/ColorGrading.fx",
  "parameters": [
    {
      "name": "Saturation",
      "type": "float",
      "defaultValue": 1.0,
      "min": 0.0,
      "max": 2.0
    },
    {
      "name": "Contrast",
      "type": "float",
      "defaultValue": 1.0,
      "min": 0.0,
      "max": 2.0
    }
  ]
}
```

### ShaderDefinition Class

**File Location**: `MonoBall.Core/Mods/Definitions/ShaderDefinition.cs`

```csharp
namespace MonoBall.Core.Mods.Definitions
{
    /// <summary>
    /// Definition for a shader effect that can be loaded from mods.
    /// </summary>
    public class ShaderDefinition
    {
        /// <summary>
        /// Unique identifier for the shader (e.g., "base:shader:ColorGrading").
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the shader.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional description of what the shader does.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Path to the .fx source file relative to mod root (e.g., "Shaders/ColorGrading.fx").
        /// </summary>
        public string SourceFile { get; set; } = string.Empty;

        /// <summary>
        /// Optional list of shader parameters with metadata.
        /// </summary>
        public List<ShaderParameterDefinition>? Parameters { get; set; }
    }

    /// <summary>
    /// Definition for a shader parameter.
    /// </summary>
    public class ShaderParameterDefinition
    {
        /// <summary>
        /// Parameter name as defined in the shader (.fx file).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Parameter type (e.g., "float", "float2", "float3", "float4", "Texture2D").
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Default value for the parameter.
        /// </summary>
        public object? DefaultValue { get; set; }

        /// <summary>
        /// Optional minimum value (for numeric types).
        /// </summary>
        public double? Min { get; set; }

        /// <summary>
        /// Optional maximum value (for numeric types).
        /// </summary>
        public double? Max { get; set; }
    }
}
```

## Build Process

### MSBuild Target

Add a new MSBuild target to `MonoBall.DesktopGL.csproj` that:

1. **Discovers shader definitions** from all mods
2. **Finds corresponding .fx files** referenced in definitions
3. **Compiles each .fx to .mgfxo** using MGFXC with `/Profile:OpenGL`
4. **Places compiled .mgfxo files** next to source .fx files in mod directories
5. **Runs before CopyMods** target to ensure compiled shaders are copied

### MGFXC Compilation

```xml
<Target Name="CompileModShaders" BeforeTargets="CopyMods">
  <PropertyGroup>
    <ModsSourcePath>$(MSBuildProjectDirectory)\..\..\Mods</ModsSourcePath>
  </PropertyGroup>

  <!-- Discover all shader definitions -->
  <ItemGroup>
    <ShaderDefinitions Include="$(ModsSourcePath)\**\Definitions\Shaders\*.json" />
  </ItemGroup>

  <!-- Extract source file paths and compile -->
  <!-- For each definition, read JSON, extract SourceFile, compile .fx to .mgfxo -->
</Target>
```

### MGFXC Command

For each shader:
```bash
dotnet mgfxc "Mods/{mod-id}/Shaders/{shader-id}.fx" "Mods/{mod-id}/Shaders/{shader-id}.mgfxo" /Profile:OpenGL
```

**Note**: MGFXC must be installed as a .NET tool (`dotnet tool install -g dotnet-mgfxc`).

## Runtime Loading

### ShaderLoader Class

```csharp
namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Loads compiled shader effects from mod directories.
    /// </summary>
    public class ShaderLoader
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the ShaderLoader.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device for creating effects.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <exception cref="ArgumentNullException">Thrown when graphicsDevice or logger is null.</exception>
        public ShaderLoader(GraphicsDevice graphicsDevice, ILogger logger)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Loads a compiled shader effect from a mod.
        /// </summary>
        /// <param name="shaderDefinition">The shader definition.</param>
        /// <param name="modManifest">The mod manifest containing the shader.</param>
        /// <returns>The loaded Effect.</returns>
        /// <exception cref="ArgumentNullException">Thrown when shaderDefinition or modManifest is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when compiled shader file is not found.</exception>
        /// <exception cref="InvalidOperationException">Thrown when shader bytecode is invalid.</exception>
        public Effect LoadShader(ShaderDefinition shaderDefinition, ModManifest modManifest)
        {
            if (shaderDefinition == null)
                throw new ArgumentNullException(nameof(shaderDefinition));
            if (modManifest == null)
                throw new ArgumentNullException(nameof(modManifest));

            string mgfxoPath = Path.Combine(
                modManifest.ModDirectory,
                Path.ChangeExtension(shaderDefinition.SourceFile, ".mgfxo")
            );

            if (!File.Exists(mgfxoPath))
            {
                throw new FileNotFoundException(
                    $"Compiled shader not found: {mgfxoPath}. " +
                    "Ensure shaders are compiled during build.",
                    mgfxoPath
                );
            }

            byte[] bytecode = File.ReadAllBytes(mgfxoPath);
            return new Effect(_graphicsDevice, bytecode);
        }
    }
}
```

### ShaderService Extension

Extend `ShaderService` to support mod shaders. **Important**: Mod shaders use a different ID format than ContentManager shaders:

- **Mod shaders**: `{namespace}:shader:{name}` (e.g., `base:shader:ColorGrading`)
- **ContentManager shaders**: `{LayerType}{Name}` (e.g., `TileLayerColorGrading`)

Add a new method for mod shaders (keep existing `LoadShader` for ContentManager compatibility):

```csharp
public class ShaderService : IShaderService
{
    private readonly ShaderLoader? _modShaderLoader;
    private readonly IModManager? _modManager; // Optional, for mod shaders

    // Existing ContentManager-based loading...
    
    /// <summary>
    /// Loads a shader from mods. Shader ID must be in format "{namespace}:shader:{name}".
    /// </summary>
    /// <param name="shaderId">The shader ID (e.g., "base:shader:ColorGrading").</param>
    /// <returns>The loaded Effect.</returns>
    /// <exception cref="InvalidOperationException">Thrown when shader is not found or mod manager is not available.</exception>
    public Effect LoadModShader(string shaderId)
    {
        if (_modManager == null)
        {
            throw new InvalidOperationException(
                "ModManager is not available. Cannot load mod shaders."
            );
        }

        if (_modShaderLoader == null)
        {
            throw new InvalidOperationException(
                "ShaderLoader is not initialized. Cannot load mod shaders."
            );
        }

        var metadata = _modManager.Registry.GetById(shaderId);
        if (metadata == null)
        {
            throw new InvalidOperationException(
                $"Shader definition '{shaderId}' not found in mod registry."
            );
        }

        if (metadata.DefinitionType != "Shaders")
        {
            throw new InvalidOperationException(
                $"Definition '{shaderId}' is not a shader definition (type: {metadata.DefinitionType})."
            );
        }

        var shaderDef = _modManager.GetDefinition<ShaderDefinition>(shaderId);
        if (shaderDef == null)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize shader definition '{shaderId}'."
            );
        }

        var modManifest = _modManager.LoadedMods
            .FirstOrDefault(m => m.Id == metadata.OriginalModId);
        if (modManifest == null)
        {
            throw new InvalidOperationException(
                $"Mod '{metadata.OriginalModId}' that owns shader '{shaderId}' not found in loaded mods."
            );
        }

        return _modShaderLoader.LoadShader(shaderDef, modManifest);
    }
}
```

## Mod Loading Integration

### ModLoader Extension

`ModLoader` already loads definitions from JSON files. Shader definitions will be automatically loaded as `DefinitionMetadata` with `DefinitionType = "Shaders"`.

**Important**: Mods must configure the `Shaders` content folder in their `mod.json`:

```json
{
  "id": "base:monoball-core",
  "name": "MonoBall Core",
  "contentFolders": {
    "Fonts": "Definitions/Fonts",
    "Behaviors": "Definitions/Behaviors",
    "Shaders": "Definitions/Shaders"
  }
}
```

The `ModLoader` will discover `Definitions/Shaders/*.json` files automatically when the `"Shaders"` entry is present in `contentFolders`.

### DefinitionRegistry Usage

```csharp
// Get shader definition
var shaderDef = modManager.GetDefinition<ShaderDefinition>("base:shader:ColorGrading");

// Get all shader definitions
var allShaders = modManager.Registry.GetByType("ShaderDefinition");
```

### Shader Usage in Rendering Systems

Shaders are referenced by their ID in rendering systems. The shader definition doesn't restrict where it can be used:

```csharp
// Example: Apply mod shader to tile layer
var shader = shaderService.LoadModShader("base:shader:ColorGrading");
spriteBatch.Begin(effect: shader);
// ... render tile layer ...
spriteBatch.End();

// Same shader can be used on sprite layer
spriteBatch.Begin(effect: shader);
// ... render sprite layer ...
spriteBatch.End();

// Or as a screen-space effect
// ... render to render target ...
spriteBatch.Begin(effect: shader);
spriteBatch.Draw(renderTarget, ...);
spriteBatch.End();

// ContentManager shaders still work with existing method
var contentShader = shaderService.GetShader("TileLayerColorGrading");
```

**Note**: Use `LoadModShader()` for mod shaders (ID format: `{namespace}:shader:{name}`) and `GetShader()` for ContentManager shaders (ID format: `{LayerType}{Name}`).

The rendering system decides where to apply the shader, not the shader definition itself.

## Error Handling

### Build-Time Errors

- **Missing .fx file**: Fail build with clear error message
- **MGFXC compilation failure**: Fail build, show MGFXC error output
- **Invalid shader definition JSON**: Fail build, show JSON parsing error

### Runtime Errors

- **Missing .mgfxo file**: Throw `FileNotFoundException` with helpful message
- **Invalid bytecode**: Let MonoGame's `Effect` constructor throw (fail fast)
- **Shader definition not found**: Return null or throw based on context

## Validation

### ModValidator Extension

Add validation for shader definitions:

```csharp
// In ModValidator
private void ValidateShaderDefinition(string definitionPath, List<ValidationIssue> issues)
{
    // 1. Parse JSON
    // 2. Check SourceFile exists
    // 3. Check SourceFile has .fx extension
    // 4. Validate parameter definitions match shader parameters (optional, requires parsing .fx)
}
```

## Example Mod Structure

```
Mods/
└── base/
    ├── mod.json
    ├── Definitions/
    │   └── Shaders/
    │       ├── ColorGrading.json
    │       └── Blur.json
    └── Shaders/
        ├── ColorGrading.fx
        └── Blur.fx

# After build:
Mods/
└── base/
    └── Shaders/
        ├── ColorGrading.fx
        ├── ColorGrading.mgfxo    # ← Compiled
        ├── Blur.fx
        └── Blur.mgfxo            # ← Compiled
```

## Implementation Steps

1. **Create ShaderDefinition classes** (`MonoBall.Core/Mods/Definitions/ShaderDefinition.cs`, `ShaderParameterDefinition.cs`)
2. **Add MSBuild target** for shader compilation (`CompileModShaders`) - see analysis document for complete implementation
3. **Create ShaderLoader class** (`MonoBall.Core/Rendering/ShaderLoader.cs`) for runtime loading
4. **Extend ShaderService** (`MonoBall.Core/Rendering/ShaderService.cs`) - add `LoadModShader()` method (keep existing methods for backward compatibility)
5. **Update GameServices** to initialize `ShaderLoader` and pass `IModManager` to `ShaderService`
6. **Add validation** in `ModValidator` for shader definitions
7. **Update mod.json examples** to include `"Shaders": "Definitions/Shaders"` in `contentFolders`
8. **Update documentation** with shader definition format and ID requirements

## Benefits

1. **No Content Pipeline**: Shaders compiled directly, no XNB files
2. **Mod-Friendly**: Shaders defined alongside other mod content
3. **Build-Time Validation**: Compilation errors caught during build
4. **OpenGL Only**: Simplified, single-profile target
5. **Consistent Pattern**: Uses existing DefinitionRegistry pattern
6. **Runtime Flexibility**: Shaders can be hot-reloaded (if desired) by re-reading .mgfxo files
7. **Shader Reusability**: Shaders are not restricted to specific layer types - can be applied anywhere

## Future Enhancements

1. **Shader Templates**: Pre-defined shader templates mods can extend
2. **Hot Reload**: Watch .mgfxo files and reload shaders at runtime
3. **Shader Parameters UI**: Auto-generate UI from parameter definitions
4. **Shader Validation**: Parse .fx files to validate parameter definitions match actual shader parameters

