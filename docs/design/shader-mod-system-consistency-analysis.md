# Shader Mod System - Consistency Analysis

## Pattern Comparison

### Current Pattern (Sprites/Tilesets)

**SpriteLoaderService.GetSpriteTexture()** and **TilesetLoaderService.LoadTileset()** follow this pattern:

1. **Get definition**: `_modManager.GetDefinition<SpriteDefinition>(spriteId)`
2. **Get metadata**: `_modManager.GetDefinitionMetadata(spriteId)`
3. **Find mod**: Iterate `_modManager.LoadedMods` to find mod where `mod.Id == metadata.OriginalModId`
4. **Resolve path**: `Path.Combine(modManifest.ModDirectory, definition.TexturePath)`
5. **Normalize path**: `Path.GetFullPath(texturePath)`
6. **Check existence**: `File.Exists(texturePath)`
7. **Load file**: `Texture2D.FromFile(_graphicsDevice, texturePath)`
8. **Cache**: Store in `_textureCache[spriteId]`

### Proposed Shader Pattern

**ShaderService.LoadModShader()** should follow the same pattern:

1. ✅ **Get definition**: `_modManager.GetDefinition<ShaderDefinition>(shaderId)` 
2. ❌ **Get metadata**: Currently uses `_modManager.Registry.GetById()` - should use `GetDefinitionMetadata()` for consistency
3. ✅ **Find mod**: Use `metadata.OriginalModId` to find mod
4. ✅ **Resolve path**: `Path.Combine(modManifest.ModDirectory, shaderDefinition.SourceFile)`
5. ❌ **Normalize path**: Missing `Path.GetFullPath()` - should add for consistency
6. ✅ **Check existence**: `File.Exists(mgfxoPath)`
7. ✅ **Load file**: `File.ReadAllBytes()` then `new Effect()` (appropriate for shaders)
8. ✅ **Cache**: Store in `_cache[shaderId]` (already implemented)

## Inconsistencies Found

### 1. Metadata Access Pattern

**Current Design**: Uses `_modManager.Registry.GetById(shaderId)`  
**Should Use**: `_modManager.GetDefinitionMetadata(shaderId)` for consistency

**Reason**: All other services use `GetDefinitionMetadata()` which is the public API. `Registry.GetById()` is internal.

### 2. Path Normalization

**Current Design**: Missing `Path.GetFullPath()`  
**Should Add**: Normalize path like sprites/tilesets do

**Reason**: Ensures consistent path handling across all resource loaders.

### 3. Mod Finding Pattern

**Current Design**: Uses `FirstOrDefault()` LINQ  
**Sprite/Tileset Pattern**: Uses `foreach` loop

**Note**: Both are fine, but `FirstOrDefault()` is more modern. However, for consistency with existing code, we could use `foreach`. Actually, looking at the code, sprites use `foreach`, so we should match that.

### 4. Error Handling

**Current Design**: Throws exceptions (per .cursorrules)  
**Sprite/Tileset Pattern**: Returns `null` on failure

**Note**: Per .cursorrules, we should throw exceptions (fail fast). This is correct for shaders, but inconsistent with sprites/tilesets. However, .cursorrules takes precedence.

## Recommended Fixes

### Update ShaderService.LoadModShader()

```csharp
public Effect LoadModShader(string shaderId)
{
    // ... validation ...
    
    // Use GetDefinitionMetadata() instead of Registry.GetById()
    var metadata = _modManager.GetDefinitionMetadata(shaderId);
    if (metadata == null)
    {
        throw new InvalidOperationException(
            $"Shader definition '{shaderId}' not found in mod registry."
        );
    }
    
    // ... rest of validation ...
    
    // Find mod manifest (use foreach for consistency)
    ModManifest? modManifest = null;
    foreach (var mod in _modManager.LoadedMods)
    {
        if (mod.Id == metadata.OriginalModId)
        {
            modManifest = mod;
            break;
        }
    }
    
    if (modManifest == null)
    {
        throw new InvalidOperationException(
            $"Mod '{metadata.OriginalModId}' that owns shader '{shaderId}' not found in loaded mods."
        );
    }
    
    // Resolve and normalize path
    string mgfxoPath = Path.Combine(
        modManifest.ModDirectory,
        Path.ChangeExtension(shaderDefinition.SourceFile, ".mgfxo")
    );
    mgfxoPath = Path.GetFullPath(mgfxoPath); // Add normalization
    
    // ... rest of loading ...
}
```

## Consistency Checklist

- [x] Get definition from registry via `GetDefinition<T>()`
- [x] Get metadata via `GetDefinitionMetadata()` (needs update)
- [x] Find mod via `metadata.OriginalModId`
- [x] Combine paths using `Path.Combine(modManifest.ModDirectory, definition.Path)`
- [ ] Normalize path using `Path.GetFullPath()` (needs update)
- [x] Check file existence before loading
- [x] Load file from filesystem
- [x] Cache loaded resources
- [x] Use same logging pattern

