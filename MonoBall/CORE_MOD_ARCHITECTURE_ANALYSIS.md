# Core Mod Architecture Analysis

## Executive Summary

The current architecture for handling the core mod (slot 0 in `mod.manifest`) combined with FontService refactoring has several architectural problems that limit flexibility and create maintenance issues. This document identifies these issues and proposes solutions.

---

## Current Architecture Problems

### 1. Hardcoded Core Mod ID

**Problem**: The core mod is identified by a hardcoded string `"base:monoball-core"` in multiple locations:

- `ModLoader.cs` line 66: `const string CoreModId = "base:monoball-core";`
- `ModLoader.cs` line 222: `if (modId == "base:monoball-core")`
- `ModLoader.cs` line 243: `mod.Id != "base:monoball-core"`
- `ModLoader.cs` line 275: `m.Id != "base:monoball-core"`
- `ModManager.cs` lines 211, 247: `m.Id == "base:monoball-core"`

**Impact**:
- Cannot change core mod ID without code changes
- Ignores the `mod.manifest` slot 0 concept entirely
- Creates tight coupling between code and mod ID
- Violates DRY principle (repeated string literal)

**Root Cause**: The code doesn't respect the `mod.manifest` slot 0 concept - it searches by hardcoded ID instead of using the first mod in the manifest order.

---

### 2. Slot 0 Concept Not Implemented

**Problem**: The `mod.manifest` file specifies load order with `modOrder`, but the code:
1. Loads core mod by hardcoded ID BEFORE reading `mod.manifest`
2. Skips the core mod when processing `mod.manifest` order
3. Never uses slot 0 (first entry) to identify the core mod

**Current Flow**:
```65:83:MonoBall/MonoBall.Core/Mods/ModLoader.cs
// Step 2: Load core mod FIRST (base:monoball-core) for system-critical resources
const string CoreModId = "base:monoball-core";
var coreMod = modManifests.FirstOrDefault(m => m.Id == CoreModId);
if (coreMod != null)
{
    _logger.Information(
        "Loading core mod '{CoreModId}' first for system-critical resources",
        CoreModId
    );
    LoadModDefinitions(coreMod, errors);
    _loadedMods.Add(coreMod);
    modManifests.Remove(coreMod); // Remove from list so it's not loaded again
}
else
{
    errors.Add(
        $"Core mod '{CoreModId}' not found. System-critical resources may not be available."
    );
}
```

**Impact**:
- `mod.manifest` slot 0 is meaningless - core mod is determined by ID, not position
- Cannot designate a different mod as "core" without code changes
- The manifest's explicit ordering is partially ignored

---

### 3. FontService Inefficient Mod Manifest Lookup

**Problem**: `FontService.GetFontSystem()` manually iterates through `LoadedMods` to find the mod manifest:

```86:95:MonoBall/MonoBall.Core/Rendering/FontService.cs
// Find mod manifest
ModManifest? modManifest = null;
foreach (var mod in _modManager.LoadedMods)
{
    if (mod.Id == metadata.OriginalModId)
    {
        modManifest = mod;
        break;
    }
}
```

**Impact**:
- O(n) lookup for every font load (should be O(1))
- Duplicates logic that should be in `IModManager`
- Similar pattern exists in `MapPopupRendererSystem.cs` (line 684)

**Root Cause**: `IModManager` doesn't provide a method to get mod manifest by ID.

---

### 4. No Core Mod Abstraction

**Problem**: There's no abstraction for "core mod" concept. Code directly checks for `"base:monoball-core"` everywhere.

**Impact**:
- Cannot easily identify "the core mod" programmatically
- Hard to extend for future "system-critical" mod concepts
- No single source of truth for what constitutes "core"

**Missing API**:
- `IModManager.CoreMod { get; }` - Get the core mod manifest
- `IModManager.IsCoreMod(string modId)` - Check if mod is core
- `IModManager.IsCoreMod(ModManifest mod)` - Check if mod is core

---

### 5. Initialization Order Complexity

**Problem**: Complex initialization flow with potential for duplicate ModManager instances:

1. `MonoBallGame.LoadCoreModSynchronously()` creates ModManager and loads ALL mods
2. `GameServices.Initialize()` tries to reuse it or creates a new one
3. FontService is created in both places

**Current Flow**:
```378:422:MonoBall/MonoBall.Core/MonoBallGame.cs
private void LoadCoreModSynchronously()
{
    _logger.Information("Loading core mod synchronously for system-critical resources");

    string? modsDirectory = Mods.Utilities.ModsPathResolver.FindModsDirectory();
    if (string.IsNullOrEmpty(modsDirectory) || !Directory.Exists(modsDirectory))
    {
        throw new InvalidOperationException(
            $"Mods directory not found: {modsDirectory}. "
                + "Cannot load core mod. Ensure Mods directory exists."
        );
    }

    // Create ModManager and load all mods (core mod loads first)
    var modManager = new Mods.ModManager(
        modsDirectory,
        LoggerFactory.CreateLogger<Mods.ModManager>()
    );

    // Load mods (core mod loads first, then others)
    var errors = new List<string>();
    bool success = modManager.Load(errors);

    if (!success)
    {
        throw new InvalidOperationException(
            $"Failed to load mods. Errors: {string.Join("; ", errors)}"
        );
    }

    // Register ModManager in Game.Services
    Services.AddService(typeof(Mods.ModManager), modManager);
    _logger.Debug("ModManager loaded and registered");

    // Create and register FontService immediately after mods load
    Mods.Utilities.FontServiceFactory.GetOrCreateFontService(
        this,
        modManager,
        GraphicsDevice,
        LoggerFactory.CreateLogger<Rendering.FontService>()
    );
    _logger.Debug("FontService created and registered");

    _logger.Information("Core mod loaded successfully, FontService available");
}
```

**Impact**:
- Method name says "core mod" but loads ALL mods
- Potential for race conditions if initialization order changes
- Hard to reason about what's loaded when

---

### 6. Missing Mod Manifest Lookup API

**Problem**: `IModManager` doesn't provide efficient lookup of mod manifests by ID.

**Current Workaround**: Services iterate through `LoadedMods`:
- `FontService.cs` line 88-94
- `MapPopupRendererSystem.cs` line 684-693

**Impact**:
- Code duplication
- Inefficient lookups
- Violates encapsulation (exposes internal list)

**Missing API**:
```csharp
ModManifest? GetModManifest(string modId);
bool TryGetModManifest(string modId, out ModManifest? manifest);
```

---

## Proposed Solutions

### Solution 1: Implement Slot 0 Core Mod Concept

**Change**: Use the first mod in `mod.manifest` `modOrder` as the core mod, falling back to hardcoded ID only if manifest doesn't exist.

**Benefits**:
- Respects manifest ordering
- Allows different mods to be "core" via configuration
- Maintains backward compatibility (fallback to hardcoded ID)

**Implementation**:
1. Read `mod.manifest` first (if exists)
2. Use `modOrder[0]` as core mod ID
3. Fall back to `"base:monoball-core"` if no manifest
4. Store core mod ID in `ModLoader`/`ModManager` for reference

---

### Solution 2: Add Core Mod Abstraction

**Change**: Add `CoreMod` property and helper methods to `IModManager`.

**Benefits**:
- Single source of truth for core mod
- Easy to check if mod is core
- Future-proof for multiple "system" mods

**API**:
```csharp
public interface IModManager
{
    // ... existing members ...
    
    /// <summary>
    /// Gets the core mod manifest (slot 0 in mod.manifest, or first loaded mod).
    /// </summary>
    ModManifest? CoreMod { get; }
    
    /// <summary>
    /// Checks if the specified mod ID is the core mod.
    /// </summary>
    bool IsCoreMod(string modId);
    
    /// <summary>
    /// Gets a mod manifest by ID.
    /// </summary>
    ModManifest? GetModManifest(string modId);
}
```

---

### Solution 3: Add Efficient Mod Manifest Lookup

**Change**: Add `GetModManifest(string modId)` to `IModManager` with O(1) lookup.

**Benefits**:
- Eliminates O(n) iterations in FontService and other services
- Encapsulates mod manifest storage
- Provides consistent API

**Implementation**:
- Store mod manifests in `Dictionary<string, ModManifest>` in `ModLoader`
- Expose lookup through `IModManager`

---

### Solution 4: Refactor FontService to Use ModManager API

**Change**: Replace manual iteration with `GetModManifest()` call.

**Before**:
```csharp
ModManifest? modManifest = null;
foreach (var mod in _modManager.LoadedMods)
{
    if (mod.Id == metadata.OriginalModId)
    {
        modManifest = mod;
        break;
    }
}
```

**After**:
```csharp
var modManifest = _modManager.GetModManifest(metadata.OriginalModId);
if (modManifest == null)
{
    _logger.Warning(...);
    return null;
}
```

**Benefits**:
- Cleaner code
- Better performance
- Consistent with other services

---

### Solution 5: Clarify Initialization Flow

**Change**: Rename `LoadCoreModSynchronously()` to `LoadModsSynchronously()` and document that it loads all mods.

**Benefits**:
- Accurate method naming
- Clearer intent
- Easier to understand initialization

**Alternative**: If truly only core mod is needed early, implement actual "load core mod only" functionality.

---

## Migration Strategy

### Phase 1: Add New APIs (Non-Breaking)
1. Add `CoreMod` property to `IModManager`
2. Add `GetModManifest(string modId)` method
3. Add `IsCoreMod(string modId)` helper
4. Implement slot 0 detection in `ModLoader`

### Phase 2: Update Services (Non-Breaking)
1. Update `FontService` to use `GetModManifest()`
2. Update `MapPopupRendererSystem` to use `GetModManifest()`
3. Update other services that iterate `LoadedMods`

### Phase 3: Remove Hardcoded IDs (Breaking)
1. Replace hardcoded `"base:monoball-core"` checks with `IsCoreMod()`
2. Update `ModLoader` to use slot 0 from manifest
3. Keep fallback to hardcoded ID for backward compatibility

### Phase 4: Cleanup (Non-Breaking)
1. Remove fallback hardcoded ID (if desired)
2. Update documentation
3. Add tests for slot 0 functionality

---

## Future Considerations

### Multiple System Mods
If future expansion requires multiple "system" mods (e.g., core, UI, audio), consider:
- `ISystemMod` interface
- `SystemMods` collection in `IModManager`
- Priority-based system mod loading

### Mod Manifest Validation
Add validation to ensure:
- Slot 0 mod exists
- Slot 0 mod has required content folders
- Slot 0 mod is valid

### Configuration-Based Core Mod
Allow `mod.manifest` to explicitly mark core mod:
```json
{
  "modOrder": ["base:monoball-core", "pokemon:emerald"],
  "coreMod": "base:monoball-core"
}
```

---

## Testing Recommendations

1. **Slot 0 Detection**: Test that first mod in `modOrder` is treated as core
2. **Fallback**: Test that hardcoded ID works when no manifest exists
3. **Lookup Performance**: Verify `GetModManifest()` is O(1)
4. **FontService**: Test font loading uses new API
5. **Backward Compatibility**: Ensure existing mods still work

---

## Conclusion

The current architecture has several issues that limit flexibility and maintainability:

1. **Hardcoded core mod ID** prevents configuration-based core mod selection
2. **Slot 0 concept not implemented** despite being in manifest
3. **Inefficient lookups** in FontService and other services
4. **No core mod abstraction** makes it hard to reason about system mods
5. **Complex initialization** with potential for duplicate instances

The proposed solutions address these issues while maintaining backward compatibility and improving the architecture for future expansion.

