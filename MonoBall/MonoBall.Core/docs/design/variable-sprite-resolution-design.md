# Variable Sprite Resolution Design

## Overview

This document describes the design for resolving variable sprite IDs at runtime. Variable sprites are sprite IDs wrapped in curly braces (e.g., `{base:sprite:npcs/generic/var_rival}`) that need to be resolved to actual sprite IDs based on current game state.

## Key Principle

**All value checking must come from game state variables stored in the ECS world.**

- Use `IFlagVariableService.GetVariable<T>()` to read game state variables
- Never use hardcoded values or assumptions about game state
- All resolution logic must query the ECS world through `IFlagVariableService`
- Variables are stored in `VariablesComponent` on the game state entity

## Problem Statement

1. **Definitions are loaded upfront** - Map definitions are loaded before game state is available, so we cannot resolve variables at definition load time
2. **Runtime resolution needed** - Sprite IDs like `{base:sprite:npcs/generic/var_rival}` need to be resolved to actual sprites (e.g., `base:sprite:npcs/generic/brendan` or `base:sprite:npcs/generic/may`) based on game variables
3. **Multiple resolution points** - Sprites are accessed at different times:
   - NPC creation (`MapLoaderSystem.CreateNpcEntity`)
   - Sprite rendering (`SpriteRendererSystem`)
   - Sprite loading (`SpriteLoaderService`)

## Design Goals

1. **Lazy resolution** - Resolve variables only when sprites are actually needed
2. **Caching** - Cache resolved sprite IDs per entity to avoid repeated lookups
3. **Transparent** - Existing systems should work with minimal changes
4. **Extensible** - Easy to add new variable mappings
5. **Fail-safe** - Handle cases where variables aren't set or resolution fails

## Architecture

### Components

#### 1. Variable Sprite Detection

**Pattern**: Sprite IDs wrapped in curly braces `{fullSpriteId}` are considered variable sprites. The entire sprite ID within the braces is the variable identifier.

**Examples**:
- `{base:sprite:npcs/generic/var_rival}` → variable sprite ID: `base:sprite:npcs/generic/var_rival`
- `{base:sprite:npcs/generic/var_player}` → variable sprite ID: `base:sprite:npcs/generic/var_player`
- `{base:sprite:npcs/generic/var_0}` → variable sprite ID: `base:sprite:npcs/generic/var_0` (legacy support)

**Detection Method**:
```csharp
public static bool IsVariableSprite(string spriteId)
{
    if (string.IsNullOrEmpty(spriteId))
        return false;
    
    // Check if sprite ID is wrapped in curly braces
    return spriteId.StartsWith("{") && spriteId.EndsWith("}");
}

public static string ExtractVariableSpriteId(string variableSpriteId)
{
    if (!IsVariableSprite(variableSpriteId))
        throw new ArgumentException("Not a variable sprite ID", nameof(variableSpriteId));
    
    // Extract sprite ID from within curly braces
    // Remove leading '{' and trailing '}'
    return variableSpriteId.Substring(1, variableSpriteId.Length - 2);
}

public static string ExtractVariableName(string variableSpriteId)
{
    // Extract the variable sprite ID first
    string fullVariableSpriteId = ExtractVariableSpriteId(variableSpriteId);
    
    // Extract variable name from the sprite ID (e.g., "var_rival" from "base:sprite:npcs/generic/var_rival")
    int lastSlash = fullVariableSpriteId.LastIndexOf('/');
    string name = lastSlash < 0 
        ? fullVariableSpriteId 
        : fullVariableSpriteId.Substring(lastSlash + 1);
    
    // Validate name is not empty
    if (string.IsNullOrWhiteSpace(name))
    {
        throw new ArgumentException(
            $"Variable sprite ID has empty name: {variableSpriteId}",
            nameof(variableSpriteId)
        );
    }
    
    // Remove "var_" prefix if present (for legacy support)
    if (name.StartsWith("var_", StringComparison.OrdinalIgnoreCase))
    {
        name = name.Substring(4);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                $"Variable sprite ID has empty name after removing 'var_' prefix: {variableSpriteId}",
                nameof(variableSpriteId)
            );
        }
    }
    
    return name;
}
```

**Note**: The curly braces wrap the entire variable sprite ID. The variable name is extracted from the sprite ID path (typically the last segment after the final `/`).

#### 2. Variable Sprite Resolver Service

**Interface**: `IVariableSpriteResolver`

```csharp
namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Service for resolving variable sprite IDs to actual sprite IDs based on game state.
    /// </summary>
    public interface IVariableSpriteResolver
    {
        /// <summary>
        /// Resolves a variable sprite ID to an actual sprite ID.
        /// </summary>
        /// <param name="variableSpriteId">The variable sprite ID wrapped in curly braces (e.g., "{base:sprite:npcs/generic/var_rival}").</param>
        /// <param name="entity">Optional entity context for caching resolved values.</param>
        /// <returns>The resolved sprite ID, or null if resolution fails.</returns>
        string? ResolveVariableSprite(string variableSpriteId, Entity? entity = null);
        
        /// <summary>
        /// Checks if a sprite ID is a variable sprite.
        /// </summary>
        /// <param name="spriteId">The sprite ID to check.</param>
        /// <returns>True if the sprite ID is a variable sprite.</returns>
        bool IsVariableSprite(string spriteId);
        
        /// <summary>
        /// Clears cached resolution for a specific entity.
        /// Note: Implementation may cache per variable sprite ID rather than per entity,
        /// in which case this method may be a no-op. Use ClearAllCache() to clear all cache.
        /// </summary>
        /// <param name="entity">The entity to clear cache for.</param>
        void ClearEntityCache(Entity entity);
        
        /// <summary>
        /// Clears all cached resolutions.
        /// </summary>
        void ClearAllCache();
    }
}
```

**Implementation**: `VariableSpriteResolver`

**Dependencies**:
- **`IFlagVariableService`** (required) - Used to read game state variables via `GetVariable<T>()`
- All value checking must come from game state variables stored in the ECS world

**Responsibilities**:
- Detect variable sprites (check for `{variableName}` markers)
- Extract variable names from curly braces
- **Read game state variables using `IFlagVariableService.GetVariable<T>()`**
- Replace `{variableName}` markers with resolved values
- Map variable names to sprite IDs based on game state
- Cache resolved values per entity
- Provide fallback sprites when resolution fails

**Resolution Process**:
1. Detect curly braces wrapping sprite ID (e.g., `{base:sprite:npcs/generic/var_rival}`)
2. Extract variable sprite ID from within braces (e.g., `base:sprite:npcs/generic/var_rival`)
3. Extract variable name from sprite ID path (e.g., `rival` from `var_rival`)
4. Resolve variable name to actual sprite ID using game state
5. Return fully resolved sprite ID

**Example**:
- Input: `{base:sprite:npcs/generic/var_rival}`
- Extract variable sprite ID: `base:sprite:npcs/generic/var_rival`
- Extract variable name: `rival` (from `var_rival`)
- Resolve: Check `base:var:player/gender` → if 1 (May), return `base:sprite:npcs/generic/brendan`
- Output: `base:sprite:npcs/generic/brendan`

**Constructor**:
```csharp
public class VariableSpriteResolver : IVariableSpriteResolver
{
    private readonly IFlagVariableService _flagVariableService;
    // Cache per variable sprite ID (shared across all entities using same variable sprite)
    // More efficient than per-entity caching since multiple NPCs may use same variable sprite
    private readonly Dictionary<string, string> _resolutionCache = new();
    
    public VariableSpriteResolver(IFlagVariableService flagVariableService)
    {
        _flagVariableService = flagVariableService 
            ?? throw new ArgumentNullException(nameof(flagVariableService));
    }
    
    public string? ResolveVariableSprite(string variableSpriteId, Entity? entity = null)
    {
        if (!IsVariableSprite(variableSpriteId))
            return variableSpriteId; // Not a variable sprite, return as-is
        
        // Check cache first (shared across all entities using this variable sprite)
        if (_resolutionCache.TryGetValue(variableSpriteId, out var cached))
        {
            return cached;
        }
        
        // Extract variable sprite ID from within curly braces
        string fullVariableSpriteId = ExtractVariableSpriteId(variableSpriteId);
        
        // Extract variable name from the sprite ID path
        string variableName = ExtractVariableName(variableSpriteId);
        
        // Resolve variable name to actual sprite ID using game state
        string? resolvedSpriteId = ResolveVariable(variableName);
        if (resolvedSpriteId == null)
            return null; // Resolution failed
        
        // Cache the resolution (shared across all entities)
        _resolutionCache[variableSpriteId] = resolvedSpriteId;
        
        // Return the resolved sprite ID (replaces entire variable sprite ID)
        return resolvedSpriteId;
    }
    
    public void ClearEntityCache(Entity entity)
    {
        // No-op: we cache per variable sprite ID, not per entity
        // This method exists for interface compatibility
        // Cache is cleared via ClearAllCache() when game state variables change
    }
    
    public void ClearAllCache()
    {
        _resolutionCache.Clear();
    }
    
    /// <summary>
    /// Clears cache when relevant game state variables change.
    /// Should be called from event handlers when variables that affect sprite resolution change.
    /// </summary>
    public void OnVariableChanged(string variableKey)
    {
        // Clear all cached resolutions since they depend on game state
        // Could be optimized to only clear affected resolutions, but simpler to clear all
        _resolutionCache.Clear();
    }
    
    // Implementation methods...
}
```

**Variable Mapping Strategy**:
- **Use `IFlagVariableService.GetVariable<T>()` to read game state variables**
- All value checking must come from game state variables stored in the ECS world
- Map variable names to sprite IDs using predefined rules based on game state
- Support custom mappings per variable name

**Example Mappings Using Game State Variables**:
```csharp
// Variable: "rival"
// Resolution: Read player gender from game state variables
private string? ResolveRivalSprite()
{
    // Read player gender from game state
    int? gender = _flagVariableService.GetVariable<int>("base:var:player/gender");
    
    if (!gender.HasValue)
    {
        // Variable not set yet, use fallback
        return "base:sprite:npcs/generic/brendan"; // Default fallback
    }
    
    // If player is May (gender = 1) → rival is Brendan
    if (gender.Value == 1)
    {
        return "base:sprite:npcs/generic/brendan";
    }
    // If player is Brendan (gender = 0) → rival is May
    else
    {
        return "base:sprite:npcs/generic/may";
    }
}

// Variable: "player"
// Resolution: Read player gender from game state variables
private string? ResolvePlayerSprite()
{
    // Read player gender from game state
    int? gender = _flagVariableService.GetVariable<int>("base:var:player/gender");
    
    if (!gender.HasValue)
    {
        // Variable not set yet, use fallback
        return "base:sprite:players/brendan/normal"; // Default fallback
    }
    
    // Map gender to player sprite
    if (gender.Value == 0)
    {
        return "base:sprite:players/brendan/normal";
    }
    else
    {
        return "base:sprite:players/may/normal";
    }
}
```

**Key Principle**: All variable resolution must query game state through `IFlagVariableService.GetVariable<T>()`. Never use hardcoded values or assumptions about game state.

**Caching Strategy**:
- Cache resolved sprite ID per variable sprite ID (shared across all entities using same variable sprite)
- More efficient: if 100 NPCs use `{base:sprite:npcs/generic/var_rival}`, resolve once and reuse
- Clear cache when relevant game state variables change (via `OnVariableChanged` event handler)
- `ClearEntityCache` is a no-op (cached per variable sprite ID, not per entity)

#### 3. Component Changes

**NpcComponent** - No changes needed. Stores the variable sprite ID as-is.

**New Component**: `VariableSpriteComponent` (optional optimization)

```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that caches resolved sprite ID for variable sprites.
    /// This is an optimization to avoid repeated resolution lookups.
    /// </summary>
    public struct VariableSpriteComponent
    {
        /// <summary>
        /// The original variable sprite ID.
        /// </summary>
        public string VariableSpriteId { get; set; }
        
        /// <summary>
        /// The resolved sprite ID (cached).
        /// </summary>
        public string ResolvedSpriteId { get; set; }
        
        /// <summary>
        /// Whether the resolution was successful.
        /// </summary>
        public bool IsResolved { get; set; }
    }
}
```

**Note**: This component is optional. We can also cache in the resolver service using entity IDs.

#### 4. Integration Points

##### MapLoaderSystem.CreateNpcEntity

**Current Flow**:
1. Validate sprite definition exists
2. Create NPC entity with `NpcComponent.SpriteId = npcDef.SpriteId`
3. Preload sprite texture

**New Flow**:
1. Check if sprite ID is variable sprite
2. If variable: Resolve to actual sprite ID (or defer resolution)
3. Validate resolved sprite definition exists
4. Create NPC entity with resolved sprite ID (or variable sprite ID + VariableSpriteComponent)
5. Preload sprite texture (using resolved ID)

**Option A: Resolve at Creation** (Recommended)
```csharp
// In CreateNpcEntity
// CRITICAL: Resolve variable sprite FIRST, before validation
string actualSpriteId = npcDef.SpriteId;
if (_variableSpriteResolver?.IsVariableSprite(npcDef.SpriteId) == true)
{
    var resolved = _variableSpriteResolver.ResolveVariableSprite(npcDef.SpriteId, Entity.Null);
    if (resolved == null)
    {
        _logger.Error(
            "Failed to resolve variable sprite {VariableSpriteId} for NPC {NpcId}",
            npcDef.SpriteId,
            npcDef.NpcId
        );
        throw new InvalidOperationException(
            $"Cannot create NPC {npcDef.NpcId}: variable sprite resolution failed. " +
            $"Variable sprite ID: {npcDef.SpriteId}"
        );
    }
    actualSpriteId = resolved;
}

// CRITICAL: Always validate the RESOLVED sprite ID, never the variable sprite ID
// Variable sprite IDs like {base:sprite:npcs/generic/var_rival} are not valid sprite definitions
SpriteValidationHelper.ValidateSpriteDefinition(
    _spriteLoader,
    _logger,
    actualSpriteId, // Always a real sprite ID, never a variable sprite ID
    "NPC",
    npcDef.NpcId,
    throwOnInvalid: true
);

var npcEntity = World.Create(
    new NpcComponent
    {
        SpriteId = actualSpriteId, // Store resolved ID
        // ...
    },
    // ...
);
```

**Option B: Defer Resolution** (Alternative)
```csharp
// Store variable sprite ID, resolve later
bool isVariable = _variableSpriteResolver?.IsVariableSprite(npcDef.SpriteId) == true;
var npcEntity = World.Create(
    new NpcComponent
    {
        SpriteId = isVariable ? npcDef.SpriteId : npcDef.SpriteId,
        // ...
    },
    isVariable ? new VariableSpriteComponent
    {
        VariableSpriteId = npcDef.SpriteId,
        ResolvedSpriteId = string.Empty,
        IsResolved = false
    } : default,
    // ...
);
```

**Recommendation**: Use Option A (resolve at creation) for simplicity and immediate validation.

##### SpriteRendererSystem

**Current Flow**:
1. Query NPCs with `NpcComponent.SpriteId`
2. Get sprite definition using `npc.SpriteId`
3. Render sprite

**New Flow** (if using Option B):
1. Query NPCs
2. Check if entity has `VariableSpriteComponent`
3. If variable and not resolved: Resolve now
4. Use resolved sprite ID for rendering

**If using Option A**: No changes needed (sprite ID already resolved).

##### SpriteLoaderService

**Current Flow**:
1. Check cache for sprite ID
2. Load from registry if not cached

**New Flow**:
1. Check if sprite ID is variable sprite
2. If variable: Resolve first, then load resolved sprite
3. Cache resolved sprite

**Implementation** (Optional - only needed if using Option B):
```csharp
public SpriteDefinition? GetSpriteDefinition(string spriteId)
{
    if (string.IsNullOrEmpty(spriteId))
        return null;
    
    // Resolve variable sprites (fallback safety net)
    // Note: If Option A is used, sprites should already be resolved in MapLoaderSystem
    string actualSpriteId = spriteId;
    if (_variableSpriteResolver?.IsVariableSprite(spriteId) == true)
    {
        var resolved = _variableSpriteResolver.ResolveVariableSprite(spriteId);
        if (resolved == null)
        {
            _logger.Warning(
                "SpriteLoaderService: Failed to resolve variable sprite {VariableSpriteId}, cannot load sprite definition",
                spriteId
            );
            return null; // Cannot load variable sprite definition
        }
        actualSpriteId = resolved;
    }
    
    // Continue with normal lookup using actualSpriteId
    if (_definitionCache.TryGetValue(actualSpriteId, out var cached))
    {
        return cached;
    }
    
    // ...
}
```

**Note**: 
- This requires `SpriteLoaderService` to have access to `IVariableSpriteResolver` (optional dependency)
- If Option A (resolve at creation) is used, this is primarily a safety net
- If resolution fails here, return `null` - variable sprite IDs are not valid sprite definitions

## Variable Mapping Configuration

### Built-in Mappings

**Rival Sprite** (`var_rival`):
- **Game State Variable**: `base:var:player/gender` (read via `IFlagVariableService.GetVariable<int>()`)
- **Logic**:
  - Read `base:var:player/gender` from game state
  - If gender == 0 (Brendan) → `base:sprite:npcs/generic/may`
  - If gender == 1 (May) → `base:sprite:npcs/generic/brendan`
  - If variable not set → fallback to `base:sprite:npcs/generic/brendan`

**Player Sprite** (`var_player`):
- **Game State Variable**: `base:var:player/gender` (read via `IFlagVariableService.GetVariable<int>()`)
- **Logic**:
  - Read `base:var:player/gender` from game state
  - If gender == 0 → `base:sprite:players/brendan/normal`
  - If gender == 1 → `base:sprite:players/may/normal`
  - If variable not set → fallback to `base:sprite:players/brendan/normal`

### Extensible Mapping System

**Option 1: Hardcoded Rules** (Simple, but not flexible)
```csharp
private string? ResolveVariable(string variableName)
{
    switch (variableName.ToLowerInvariant())
    {
        case "rival":
            return ResolveRivalSprite();
        case "player":
            return ResolvePlayerSprite();
        default:
            return null;
    }
}

private string? ResolveRivalSprite()
{
    // Read from game state variables
    int? gender = _flagVariableService.GetVariable<int>("base:var:player/gender");
    
    if (!gender.HasValue)
    {
        return "base:sprite:npcs/generic/brendan"; // Fallback
    }
    
    return gender.Value == 1 
        ? "base:sprite:npcs/generic/brendan"  // Player is May, rival is Brendan
        : "base:sprite:npcs/generic/may";      // Player is Brendan, rival is May
}

private string? ResolvePlayerSprite()
{
    // Read from game state variables
    int? gender = _flagVariableService.GetVariable<int>("base:var:player/gender");
    
    if (!gender.HasValue)
    {
        return "base:sprite:players/brendan/normal"; // Fallback
    }
    
    return gender.Value == 0
        ? "base:sprite:players/brendan/normal"
        : "base:sprite:players/may/normal";
}
```

**Option 2: Mapping Registry** (More flexible)
```csharp
public class VariableSpriteResolver : IVariableSpriteResolver
{
    private readonly IFlagVariableService _flagVariableService;
    private readonly Dictionary<string, Func<string?>> _mappings = new();
    
    public VariableSpriteResolver(IFlagVariableService flagVariableService)
    {
        _flagVariableService = flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
        
        // Register built-in mappings
        RegisterMapping("rival", ResolveRivalSprite);
        RegisterMapping("player", ResolvePlayerSprite);
    }
    
    public void RegisterMapping(string variableName, Func<string?> resolver)
    {
        _mappings[variableName] = resolver;
    }
    
    private string? ResolveVariable(string variableName)
    {
        if (_mappings.TryGetValue(variableName, out var resolver))
        {
            return resolver();
        }
        return null;
    }
    
    private string? ResolveRivalSprite()
    {
        // All resolution must read from game state variables
        int? gender = _flagVariableService.GetVariable<int>("base:var:player/gender");
        // ... resolution logic ...
    }
}
```

**Important**: All resolver functions must use `_flagVariableService.GetVariable<T>()` to read game state. Never use hardcoded values or external state.

**Option 3: Definition-Based** (Most flexible, but requires definition files)
- Create variable sprite definition files that specify resolution rules
- Load at startup
- More complex but allows mods to define custom variable sprites

**Recommendation**: Start with Option 1 (hardcoded rules) for built-in variables, add Option 2 (registry) for extensibility.

## Error Handling

### Resolution Failures

**Scenarios**:
1. Variable not set (e.g., game just started, gender not chosen)
2. Invalid variable name
3. Resolved sprite ID doesn't exist
4. Variable value doesn't map to any sprite

**Handling Strategy**:
1. **Log error** when resolution fails (not just warning - this is a critical failure)
2. **Throw exception** in MapLoaderSystem if resolution fails (Option A) - cannot create NPC with unresolved variable sprite
3. **Return null** in SpriteLoaderService if resolution fails - cannot load sprite definition for variable sprite
4. **Fallback sprites** are built into resolver logic (e.g., default rival sprite when gender not set)
5. **Never validate variable sprite IDs** - always validate resolved sprite IDs only

**Fallback Sprites**:
- `var_rival` → `base:sprite:npcs/generic/brendan` (default)
- `var_player` → `base:sprite:players/brendan/normal` (default)

## Performance Considerations

1. **Caching**: Cache resolved sprite IDs per variable sprite ID (shared across entities)
   - If 100 NPCs use `{base:sprite:npcs/generic/var_rival}`, resolve once and reuse for all
   - More efficient than per-entity caching
2. **Lazy Resolution**: Only resolve when sprite is actually accessed (at NPC creation)
3. **Cache Invalidation**: Clear cache when relevant game state variables change (via `OnVariableChanged`)
   - Call `ClearAllCache()` from event handlers when variables affecting sprite resolution change
4. **Entity Cache Cleanup**: Not needed - cache is per variable sprite ID, not per entity
   - No memory leaks from destroyed entities

## Implementation Plan

### Phase 1: Core Infrastructure
1. Create `IVariableSpriteResolver` interface
2. Implement `VariableSpriteResolver` with detection methods
3. Add basic variable mapping (rival, player)

### Phase 2: Integration
1. Integrate resolver into `MapLoaderSystem` (Option A: resolve at creation)
   - Resolve BEFORE validation (critical)
   - Throw exception if resolution fails
2. Integrate resolver into `SpriteLoaderService` (optional safety net)
   - Return null if resolution fails (variable sprite IDs are not valid sprite definitions)
3. Add cache cleanup mechanism (call `ClearAllCache()` when game state variables change)
4. Add logging and error handling

### Phase 3: Testing & Validation
1. Test with `{base:sprite:npcs/generic/var_rival}` in littleroot_town
2. Test with unset variables (fallback behavior)
3. Test performance (caching effectiveness)

### Phase 4: Extensibility (Optional)
1. Add mapping registry for custom variables
2. Add definition-based variable sprites (if needed)

## Example Usage

### Definition (JSON)
```json
{
  "npcId": "base:npc:hoenn/littleroot_town/localid_littleroot_rival",
  "name": "LOCALID_LITTLEROOT_RIVAL",
  "spriteId": "{base:sprite:npcs/generic/var_rival}",
  "x": 208,
  "y": 160
}
```

### Runtime Resolution
1. `MapLoaderSystem` detects curly braces wrapping sprite ID
2. Calls `_variableSpriteResolver.ResolveVariableSprite("{base:sprite:npcs/generic/var_rival}")`
3. Resolver extracts variable sprite ID: `base:sprite:npcs/generic/var_rival`
4. Resolver extracts variable name: `rival` (from `var_rival`)
5. Resolver checks `base:var:player/gender` from game state
6. If gender == 1 (May), returns `base:sprite:npcs/generic/brendan`
7. NPC created with resolved sprite ID
8. Sprite loads and renders normally

## Open Questions

1. **When to resolve?** At creation (Option A) or lazily (Option B)?
   - **Answer**: Option A (at creation) for simplicity and immediate validation

2. **How to handle variable changes?** If player changes gender mid-game, should sprites update?
   - **Answer**: Typically variables don't change mid-game, but if needed, clear cache and re-resolve

3. **Legacy support**: Should we support `var_0`, `var_1` patterns?
   - **Answer**: Yes, map to descriptive names (`var_0` → `var_rival`, `var_1` → `var_player`)

4. **Definition validation**: Should variable sprites be validated at definition load time?
   - **Answer**: No, defer validation until resolution (variables may not be set yet)

5. **Component vs Service caching**: Store resolved ID in component or service cache?
   - **Answer**: Service cache (simpler, no component changes needed)

