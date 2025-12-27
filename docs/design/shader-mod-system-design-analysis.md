# Shader Mod System Design - Architecture Analysis

## Overview

This document analyzes the shader mod system design for architecture problems, .cursorrules compliance, and alignment with the current mod system.

## Critical Issues Found

### 1. ❌ Violates "No Fallback Code" Rule

**Location**: `ShaderService Extension` section (lines 252-274)

**Problem**: The design shows fallback code that tries mod shaders first, then falls back to ContentManager:

```csharp
// Try mod shader first
if (_modManager != null)
{
    var shaderDef = _modManager.GetDefinition<ShaderDefinition>(shaderId);
    if (shaderDef != null)
    {
        // ... load from mod ...
    }
}

// Fall back to ContentManager (existing behavior)
return LoadShaderFromContent(shaderId);
```

**Violation**: `.cursorrules` states: "NEVER introduce fallback code - code should fail fast with clear errors rather than silently degrade"

**Fix**: 
- Remove fallback behavior
- If shader ID matches mod shader pattern (e.g., `base:shader:ColorGrading`), load from mods only
- If shader ID matches ContentManager pattern (e.g., `TileLayerColorGrading`), load from ContentManager only
- Throw `InvalidOperationException` if shader not found in the expected location

### 2. ❌ Missing Constructor Validation

**Location**: `ShaderLoader` class (lines 197-202)

**Problem**: `ShaderLoader` constructor doesn't validate parameters:

```csharp
public class ShaderLoader
{
    private readonly string _modsDirectory;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    // Missing constructor!
}
```

**Violation**: `.cursorrules` states: "Validate arguments and throw `ArgumentNullException` or `ArgumentException` with parameter names"

**Fix**: Add constructor with validation:
```csharp
public ShaderLoader(GraphicsDevice graphicsDevice, ILogger logger)
{
    _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
}
```

### 3. ❌ Incorrect Mod Directory Resolution

**Location**: `ShaderService Extension` (lines 260-263)

**Problem**: The design searches for mods by checking if `SourceFile` exists:

```csharp
var modManifest = _modManager.LoadedMods
    .FirstOrDefault(m => 
        File.Exists(Path.Combine(m.ModDirectory, shaderDef.SourceFile))
    );
```

**Issues**:
- Inefficient - iterates through all mods
- Doesn't handle load order properly (should use the mod that owns the definition)
- `DefinitionMetadata` already has `OriginalModId` - should use that

**Fix**: Use `DefinitionMetadata.OriginalModId` to find the owning mod:
```csharp
var metadata = _modManager.Registry.GetById(shaderId);
if (metadata == null)
{
    throw new InvalidOperationException($"Shader definition '{shaderId}' not found.");
}

var modManifest = _modManager.LoadedMods.FirstOrDefault(m => m.Id == metadata.OriginalModId);
if (modManifest == null)
{
    throw new InvalidOperationException(
        $"Mod '{metadata.OriginalModId}' that owns shader '{shaderId}' not found in loaded mods."
    );
}
```

### 4. ❌ Missing ContentFolders Configuration

**Location**: `Mod Loading Integration` section (line 282-284)

**Problem**: The design states "No changes needed to ModLoader" but doesn't explain that mods must configure `contentFolders` in `mod.json`:

**Current Mod System**: Definitions are loaded from folders specified in `mod.json`:
```json
{
  "contentFolders": {
    "Fonts": "Definitions/Fonts",
    "Behaviors": "Definitions/Behaviors",
    "Shaders": "Definitions/Shaders"  // ← Must be added!
  }
}
```

**Fix**: Document that mods must include `"Shaders": "Definitions/Shaders"` in `contentFolders`.

### 5. ❌ Namespace Mismatch

**Location**: `ShaderDefinition` class (line 82)

**Problem**: Namespace is `MonoBall.Core.Mods.Definitions` but the design doesn't specify where the file should be located.

**Current Structure**: Based on `.cursorrules`, namespace should match folder structure:
- `MonoBall.Core.Mods.Definitions` → `MonoBall.Core/Mods/Definitions/`

**Fix**: Specify file location: `MonoBall.Core/Mods/Definitions/ShaderDefinition.cs`

### 6. ❌ ShaderLoader Missing Required Dependencies

**Location**: `ShaderLoader` class (line 199)

**Problem**: `ShaderLoader` stores `_modsDirectory` but doesn't need it - it receives `modDirectory` as a parameter.

**Issue**: The loader should get mod directory from `ModManifest`, not store a global mods directory.

**Fix**: Remove `_modsDirectory` field, get mod directory from `ModManifest` parameter.

### 7. ❌ Missing XML Documentation

**Location**: `ShaderLoader` class

**Problem**: Missing XML documentation comments on public methods.

**Violation**: `.cursorrules` states: "Document all public APIs with XML comments"

**Fix**: Add XML documentation to all public methods.

### 8. ❌ ShaderService Extension Breaks Existing API

**Location**: `ShaderService Extension` (line 252)

**Problem**: The design modifies `LoadShader(string shaderId)` to support both mod shaders and ContentManager shaders, but:
- Existing code expects `LoadShader` to work with ContentManager paths
- The method signature doesn't indicate which source it uses
- Breaking change without clear migration path

**Fix**: 
- Keep `LoadShader(string shaderId)` for ContentManager only (backward compatible)
- Add new method `LoadModShader(string shaderId)` for mod shaders
- Or: Use shader ID format to determine source (mod IDs use `:` separator, ContentManager uses prefixes)

### 9. ❌ MSBuild Target Incomplete

**Location**: `Build Process` section (lines 163-175)

**Problem**: The MSBuild target is incomplete - it doesn't show how to:
- Parse JSON files to extract `SourceFile`
- Handle errors properly
- Ensure MGFXC tool is available

**Fix**: Provide complete MSBuild target implementation or reference a custom MSBuild task.

### 10. ❌ Missing Error Handling for Mod Shader Loading

**Location**: `ShaderService Extension` (lines 257-268)

**Problem**: No error handling if:
- `GetDefinition<ShaderDefinition>` returns null (should throw)
- Mod manifest not found (should throw)
- Shader file doesn't exist (handled in ShaderLoader, but should fail fast)

**Violation**: `.cursorrules` states: "Fail fast with clear exceptions"

**Fix**: Add proper error handling with descriptive exceptions.

## Architecture Improvements Needed

### 1. Shader ID Format

**Current Design**: Uses `base:shader:ColorGrading` format

**Issue**: Need to distinguish mod shader IDs from ContentManager shader IDs:
- Mod shaders: `{namespace}:shader:{name}` (e.g., `base:shader:ColorGrading`)
- ContentManager shaders: `{LayerType}{Name}` (e.g., `TileLayerColorGrading`)

**Recommendation**: Document ID format requirements and add validation.

### 2. ShaderLoader Should Not Store Mods Directory

**Current**: `ShaderLoader` stores `_modsDirectory` field

**Better**: `ShaderLoader` should only need `GraphicsDevice` and `ILogger`. Mod directory comes from `ModManifest` parameter.

### 3. Missing ModManifest Parameter

**Current**: `LoadShader(ShaderDefinition shaderDefinition, string modDirectory)`

**Better**: `LoadShader(ShaderDefinition shaderDefinition, ModManifest modManifest)`
- Type-safe
- Access to mod metadata if needed
- Clearer intent

## Alignment with Current Mod System

### ✅ Correctly Uses DefinitionRegistry

The design correctly uses `DefinitionRegistry` pattern - shader definitions are loaded as `DefinitionMetadata` with `DefinitionType = "Shaders"`.

### ✅ Correctly Uses ModLoader Pattern

The design correctly assumes `ModLoader` will discover `Definitions/Shaders/*.json` files, but must document the `contentFolders` requirement.

### ✅ Correctly Uses DefinitionMetadata

The design correctly uses `DefinitionMetadata` to store shader definitions, allowing for modify/extend/replace operations.

### ⚠️ Missing Load Order Consideration

**Issue**: When multiple mods define the same shader ID, load order matters. The design doesn't explain how this is handled.

**Current System**: Later mods can modify/extend/replace earlier mods' definitions using `$operation`.

**Fix**: Document that shader definitions follow the same modify/extend/replace pattern as other definitions.

## Recommended Fixes Summary

1. **Remove fallback code** - Use shader ID format to determine source, fail fast if not found
2. **Add constructor validation** - Validate all parameters in `ShaderLoader`
3. **Use DefinitionMetadata.OriginalModId** - Find owning mod from metadata, not by searching files
4. **Document contentFolders requirement** - Explain that mods must configure `"Shaders": "Definitions/Shaders"`
5. **Fix namespace/file location** - Specify `MonoBall.Core/Mods/Definitions/ShaderDefinition.cs`
6. **Remove unnecessary fields** - Remove `_modsDirectory` from `ShaderLoader`
7. **Add XML documentation** - Document all public APIs
8. **Clarify API design** - Either separate methods or document ID format requirements
9. **Complete MSBuild target** - Provide full implementation or reference custom task
10. **Add error handling** - Fail fast with clear exceptions

## Updated Design Recommendations

### ShaderLoader Constructor

```csharp
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
```

### ShaderLoader.LoadShader Method

```csharp
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
```

### ShaderService Extension (No Fallback)

```csharp
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
```


