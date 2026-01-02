# Why ConstantsService Uses GetByType() While Other Services Don't

## Key Architectural Difference

### ConstantsService Pattern (Uses GetByType)

**Why**: Constants are fundamentally different from other definitions:

1. **Multiple Values Per File**: Each constants definition file contains **multiple constants** in a `constants` dictionary:
   ```json
   {
     "id": "base:constants:game",
     "constants": {
       "TileChunkSize": 16,
       "TileWidth": 16,
       "TileHeight": 16,
       "InputBufferTimeoutSeconds": 0.2,
       "InputBufferMaxSize": 5
     }
   }
   ```

2. **Merging Required**: Constants from all definition files must be **merged/flattened** into a single dictionary:
   - `base:constants:game` → `{ "TileChunkSize": 16, ... }`
   - `base:constants:player` → `{ "PlayerSpriteSheetId": "...", ... }`
   - **Result**: `{ "TileChunkSize": 16, "PlayerSpriteSheetId": "...", ... }`

3. **Flat Key-Value API**: Provides a simple API:
   ```csharp
   var chunkSize = _constantsService.Get<int>("TileChunkSize");
   ```
   Rather than definition-based API:
   ```csharp
   var gameConstants = _modManager.GetDefinition<ConstantDefinition>("base:constants:game");
   var chunkSize = gameConstants.Constants["TileChunkSize"];
   ```

4. **Mod Override Support**: Later mods override earlier mods' constants (same key = override)

**Implementation**:
```csharp
private void LoadConstantsFromMods(IModManager modManager)
{
    // Need ALL constant definitions to merge them
    var constantDefinitions = modManager.Registry.GetByType("Constants");
    
    foreach (var defId in constantDefinitions)
    {
        var definition = modManager.GetDefinition<ConstantDefinition>(defId);
        // Merge constants into flat dictionary
        foreach (var kvp in definition.Constants)
            _rawConstants[kvp.Key] = kvp.Value; // Later mods override earlier ones
    }
}
```

---

### Other Services Pattern (Use GetById)

**Why**: Other definitions follow a different pattern:

1. **One Value Per File**: Each definition file = **one definition** with **one ID**:
   ```json
   {
     "id": "base:font:game/pokemon",
     "fontPath": "Fonts/pokemon.ttf",
     "defaultSize": 16
   }
   ```

2. **On-Demand Loading**: Definitions are loaded when requested by ID:
   ```csharp
   var fontDef = _modManager.GetDefinition<FontDefinition>("base:font:game/pokemon");
   ```

3. **Definition-Based API**: Services wrap registry access:
   ```csharp
   public FontSystem LoadFont(string resourceId)
   {
       var fontDef = _modManager.GetDefinition<FontDefinition>(resourceId);
       // Load font file and return FontSystem
   }
   ```

4. **No Merging Needed**: Each definition is independent

**Implementation**:
```csharp
public FontSystem LoadFont(string resourceId)
{
    // Load specific definition by ID (on-demand)
    var fontDef = _modManager.GetDefinition<FontDefinition>(resourceId);
    if (fontDef == null)
        throw new InvalidOperationException($"Font not found: {resourceId}");
    
    // Load font file and return
    return LoadFontFromPath(fontDef.FontPath);
}
```

---

## Similar Pattern: ScriptLoaderService

**ScriptLoaderService** also uses `GetByType()` but for a different reason:

```csharp
public void PreloadAllScripts()
{
    // Need ALL script definitions to compile them upfront
    var scriptDefinitionIds = _registry.GetByType("Script");
    foreach (var scriptDefId in scriptDefinitionIds)
    {
        var scriptDef = _registry.GetById<ScriptDefinition>(scriptDefId);
        LoadScriptFromDefinition(scriptDef); // Compile script
    }
}
```

**Why**: Scripts need to be **compiled** before use, so they're preloaded upfront.

---

## Summary

| Service | Pattern | Reason |
|---------|---------|--------|
| **ConstantsService** | `GetByType()` | Need to merge/flatten constants from all files |
| **ScriptLoaderService** | `GetByType()` | Need to compile all scripts upfront |
| **FontService/ResourceManager** | `GetById()` | Load on-demand by ID |
| **SpriteLoaderService** | `GetById()` | Load on-demand by ID |

**Conclusion**: ConstantsService uses `GetByType()` because it needs to **load and merge ALL constant definitions** upfront to provide a flat key-value API. This is correct architecture - constants are fundamentally different from other definitions because they're merged/flattened across multiple files.
