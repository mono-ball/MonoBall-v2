# Variable Sprite Resolution Design - Architecture Analysis

## Critical Issues Found

### 1. ❌ **Entity Dictionary Key Problem** (Arch ECS)

**Issue**: Using `Dictionary<Entity, string>` for entity caching is problematic.

**Problem**:

- `Entity` in Arch ECS is a struct that contains an `Id` (int) and `WorldId` (int)
- Using structs as dictionary keys can work, but there's a critical issue: **Entity references become invalid when
  entities are destroyed**
- Destroyed entities may be reused (recycled) by Arch ECS, causing cache collisions
- The cache will accumulate stale entries for destroyed entities

**Current Design**:

```csharp
private readonly Dictionary<Entity, string> _entityCache = new();
```

**Impact**: Memory leak, potential incorrect resolutions, cache pollution

**Solution Options**:

1. **Use Entity.Id as key** (recommended):
   ```csharp
   private readonly Dictionary<int, string> _entityCache = new();
   
   public string? ResolveVariableSprite(string variableSpriteId, Entity? entity = null)
   {
       // ... resolution logic ...
       
       if (entity.HasValue)
       {
           int entityId = entity.Value.Id;
           if (_entityCache.TryGetValue(entityId, out var cached))
           {
               return cached;
           }
           
           // ... resolve ...
           _entityCache[entityId] = resolvedSpriteId;
       }
   }
   ```

2. **Clear cache on entity destruction** (requires event subscription):
    - Subscribe to entity destruction events
    - Clear cache entries when entities are destroyed
    - More complex but safer

3. **Don't cache per entity** (simplest):
    - Cache per variable sprite ID instead
    - Multiple NPCs with same variable sprite share cache
    - Less memory efficient but simpler

**Recommendation**: Use Option 1 (Entity.Id) + Option 2 (cleanup on destruction) for robustness.

---

### 2. ⚠️ **Entity Lifecycle and Cache Cleanup**

**Issue**: No mechanism to clean up cache when entities are destroyed.

**Problem**:

- Entities are destroyed when maps are unloaded (`MapLoaderSystem.UnloadMap`)
- Cache entries remain in `_entityCache` dictionary forever
- Memory leak accumulates over time
- Destroyed entity IDs may be reused, causing incorrect cache hits

**Current Design**: No cleanup mechanism specified.

**Solution**:

```csharp
public class VariableSpriteResolver : IVariableSpriteResolver, IDisposable
{
    private readonly IFlagVariableService _flagVariableService;
    private readonly Dictionary<int, string> _entityCache = new();
    private bool _disposed = false;
    
    public VariableSpriteResolver(IFlagVariableService flagVariableService)
    {
        _flagVariableService = flagVariableService 
            ?? throw new ArgumentNullException(nameof(flagVariableService));
        
        // Subscribe to entity destruction events if available
        // Or rely on MapLoaderSystem to call ClearEntityCache when unloading
    }
    
    public void ClearEntityCache(Entity entity)
    {
        if (_entityCache.ContainsKey(entity.Id))
        {
            _entityCache.Remove(entity.Id);
        }
    }
    
    public void ClearAllCache()
    {
        _entityCache.Clear();
    }
}
```

**Integration Point**: `MapLoaderSystem.UnloadMap` should call `ClearEntityCache` for each NPC entity before destroying
it.

---

### 3. ⚠️ **Validation Timing Issue**

**Issue**: Sprite validation happens BEFORE variable resolution in the proposed flow.

**Current Flow** (from design):

```csharp
// Step 1: Check if variable sprite
if (_variableSpriteResolver?.IsVariableSprite(npcDef.SpriteId) == true)
{
    var resolved = _variableSpriteResolver.ResolveVariableSprite(npcDef.SpriteId, Entity.Null);
    // ...
}

// Step 2: Validate sprite definition
SpriteValidationHelper.ValidateSpriteDefinition(
    _spriteLoader,
    _logger,
    actualSpriteId, // Use resolved ID
    "NPC",
    npcDef.NpcId,
    throwOnInvalid: true
);
```

**Problem**:

- If resolution fails and returns `null`, validation will fail with the variable sprite ID
  `{base:sprite:npcs/generic/var_rival}`
- Variable sprite IDs are not valid sprite definitions - they're placeholders
- Validation should happen AFTER resolution, not before

**Solution**: Ensure validation always uses resolved sprite ID:

```csharp
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
            $"Cannot create NPC {npcDef.NpcId}: variable sprite resolution failed"
        );
    }
    actualSpriteId = resolved;
}

// Now validate the RESOLVED sprite ID
SpriteValidationHelper.ValidateSpriteDefinition(
    _spriteLoader,
    _logger,
    actualSpriteId, // Always a real sprite ID, never a variable sprite ID
    "NPC",
    npcDef.NpcId,
    throwOnInvalid: true
);
```

---

### 4. ⚠️ **Multiple Resolution Points - Inconsistency Risk**

**Issue**: Resolution can happen in multiple places (MapLoaderSystem, SpriteLoaderService).

**Problem**:

- If resolution logic changes or has bugs, different systems might resolve differently
- Cache might be inconsistent between systems
- SpriteLoaderService resolution might use different cache than MapLoaderSystem

**Current Design**:

- MapLoaderSystem resolves at creation (Option A)
- SpriteLoaderService also resolves (safety net)

**Risk**: If SpriteLoaderService resolves differently or uses different cache, could cause issues.

**Solution**:

- Ensure both use the same resolver instance (dependency injection)
- Consider making SpriteLoaderService resolution a fallback only (log warning if it needs to resolve)
- Or remove SpriteLoaderService resolution entirely if Option A is used

---

### 5. ⚠️ **Variable Name Extraction Edge Cases**

**Issue**: `ExtractVariableName` has potential edge cases.

**Current Implementation**:

```csharp
public static string ExtractVariableName(string variableSpriteId)
{
    string fullVariableSpriteId = ExtractVariableSpriteId(variableSpriteId);
    int lastSlash = fullVariableSpriteId.LastIndexOf('/');
    if (lastSlash < 0)
        return fullVariableSpriteId; // No slash, return entire ID
    
    string name = fullVariableSpriteId.Substring(lastSlash + 1);
    
    // Remove "var_" prefix if present
    if (name.StartsWith("var_", StringComparison.OrdinalIgnoreCase))
    {
        return name.Substring(4);
    }
    
    return name;
}
```

**Edge Cases**:

1. **No slash**: `{base:sprite:var_rival}` → returns `base:sprite:var_rival` (entire ID)
    - This might be intentional, but could cause issues if resolver expects just `rival`

2. **Multiple `var_` prefixes**: `{base:sprite:npcs/var_var_rival}` → extracts `var_var_rival`, removes first `var_` →
   `var_rival`
    - Probably not a real case, but should handle gracefully

3. **Empty name after prefix removal**: `{base:sprite:npcs/generic/var_}` → returns empty string
    - Should validate and throw or return null

**Solution**: Add validation:

```csharp
public static string ExtractVariableName(string variableSpriteId)
{
    string fullVariableSpriteId = ExtractVariableSpriteId(variableSpriteId);
    int lastSlash = fullVariableSpriteId.LastIndexOf('/');
    
    string name = lastSlash < 0 
        ? fullVariableSpriteId 
        : fullVariableSpriteId.Substring(lastSlash + 1);
    
    if (string.IsNullOrWhiteSpace(name))
    {
        throw new ArgumentException(
            $"Variable sprite ID has empty name: {variableSpriteId}",
            nameof(variableSpriteId)
        );
    }
    
    // Remove "var_" prefix if present
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

---

### 6. ⚠️ **Null Safety in Resolver**

**Issue**: `ResolveVariableSprite` can return `null`, but callers might not handle it properly.

**Current Design**:

```csharp
string? ResolveVariableSprite(string variableSpriteId, Entity? entity = null);
```

**Problem**:

- If resolution fails, returns `null`
- MapLoaderSystem code shows handling, but SpriteLoaderService code doesn't show full error handling
- Need to ensure all callers handle `null` appropriately

**Solution**: Ensure all callers check for null and handle appropriately (already shown in MapLoaderSystem example, but
document this requirement).

---

### 7. ⚠️ **Dependency Injection Requirements**

**Issue**: Multiple systems need access to `IVariableSpriteResolver`.

**Current Design**:

- MapLoaderSystem needs resolver
- SpriteLoaderService needs resolver (optional)

**Problem**:

- Both systems need resolver injected
- If resolver is null in SpriteLoaderService, variable sprites won't resolve there
- Need to ensure resolver is always available when needed

**Solution**:

- Make resolver required in MapLoaderSystem (already shown)
- Make resolver optional in SpriteLoaderService (already shown, but document that it's a fallback)
- Consider making resolver a singleton service

---

### 8. ⚠️ **Performance: Cache Key Strategy**

**Issue**: Caching per entity might not be optimal.

**Current Design**: Cache per entity ID.

**Analysis**:

- If 100 NPCs all use `{base:sprite:npcs/generic/var_rival}`, we resolve 100 times
- All resolve to the same sprite ID (assuming same game state)
- Could cache per variable sprite ID instead

**Optimization**:

```csharp
// Cache per variable sprite ID (shared across entities)
private readonly Dictionary<string, string> _resolutionCache = new();

public string? ResolveVariableSprite(string variableSpriteId, Entity? entity = null)
{
    if (!IsVariableSprite(variableSpriteId))
        return variableSpriteId;
    
    // Check shared cache first
    if (_resolutionCache.TryGetValue(variableSpriteId, out var cached))
    {
        return cached;
    }
    
    // Resolve...
    string? resolved = ResolveVariable(variableName);
    
    if (resolved != null)
    {
        _resolutionCache[variableSpriteId] = resolved;
    }
    
    return resolved;
}
```

**Trade-off**:

- Pro: More efficient (resolve once per variable sprite ID)
- Con: If game state changes, cache becomes stale
- Solution: Clear cache when relevant variables change (via events)

**Recommendation**: Use per-variable-sprite-ID cache + clear on variable change events.

---

### 9. ⚠️ **Thread Safety** (if applicable)

**Issue**: If systems run on multiple threads, cache access needs synchronization.

**Current Design**: No thread safety considerations.

**Analysis**:

- Arch ECS systems typically run on single thread
- But if SpriteLoaderService is accessed from multiple threads, cache needs locking

**Solution**: Add thread safety if needed:

```csharp
private readonly Dictionary<string, string> _resolutionCache = new();
private readonly object _cacheLock = new object();

public string? ResolveVariableSprite(string variableSpriteId, Entity? entity = null)
{
    lock (_cacheLock)
    {
        if (_resolutionCache.TryGetValue(variableSpriteId, out var cached))
        {
            return cached;
        }
        
        // ... resolve ...
        
        if (resolved != null)
        {
            _resolutionCache[variableSpriteId] = resolved;
        }
        
        return resolved;
    }
}
```

**Note**: Only needed if multi-threaded access is possible. Check if MonoGame/Arch ECS is single-threaded.

---

### 10. ⚠️ **Circular Dependency Risk**

**Issue**: Resolver depends on FlagVariableService, which might depend on World.

**Current Design**:

- VariableSpriteResolver → IFlagVariableService
- MapLoaderSystem → IVariableSpriteResolver + ISpriteLoaderService

**Analysis**:

- No circular dependency detected
- But need to ensure resolver is created before systems that use it

**Solution**: Ensure proper initialization order in SystemManager/DI container.

---

## Summary of Required Fixes

### Critical (Must Fix):

1. ✅ Fix Entity dictionary key issue (use Entity.Id instead)
2. ✅ Add cache cleanup on entity destruction
3. ✅ Fix validation timing (validate resolved sprite, not variable sprite)

### Important (Should Fix):

4. ✅ Add validation to ExtractVariableName for edge cases
5. ✅ Document null handling requirements
6. ✅ Optimize cache strategy (per variable sprite ID, not per entity)

### Nice to Have:

7. ⚠️ Add thread safety if multi-threaded access is possible
8. ⚠️ Consider removing SpriteLoaderService resolution if Option A is used

---

## Recommended Design Changes

### Updated Resolver Implementation:

```csharp
public class VariableSpriteResolver : IVariableSpriteResolver
{
    private readonly IFlagVariableService _flagVariableService;
    // Cache per variable sprite ID (shared across entities)
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
        
        // Extract variable sprite ID and name
        string fullVariableSpriteId = ExtractVariableSpriteId(variableSpriteId);
        string variableName = ExtractVariableName(variableSpriteId);
        
        // Resolve variable name to actual sprite ID using game state
        string? resolvedSpriteId = ResolveVariable(variableName);
        if (resolvedSpriteId == null)
        {
            return null; // Resolution failed
        }
        
        // Cache the resolution
        _resolutionCache[variableSpriteId] = resolvedSpriteId;
        
        return resolvedSpriteId;
    }
    
    public void ClearEntityCache(Entity entity)
    {
        // No-op: we cache per variable sprite ID, not per entity
        // This method exists for interface compatibility
    }
    
    public void ClearAllCache()
    {
        _resolutionCache.Clear();
    }
    
    // Clear cache when relevant variables change (call from event handler)
    public void OnVariableChanged(string variableKey)
    {
        // Clear all cached resolutions (they depend on game state)
        // Could be optimized to only clear affected resolutions
        _resolutionCache.Clear();
    }
}
```

### Updated MapLoaderSystem Integration:

```csharp
private Entity CreateNpcEntity(...)
{
    // Resolve variable sprite FIRST
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
    
    // NOW validate the resolved sprite ID (never validate variable sprite IDs)
    SpriteValidationHelper.ValidateSpriteDefinition(
        _spriteLoader,
        _logger,
        actualSpriteId, // Always a real sprite ID
        "NPC",
        npcDef.NpcId,
        throwOnInvalid: true
    );
    
    // ... rest of entity creation using actualSpriteId ...
}
```

---

## Testing Recommendations

1. **Test entity destruction cleanup**: Create NPCs with variable sprites, destroy them, verify cache doesn't leak
2. **Test validation**: Ensure variable sprite IDs are never validated (only resolved IDs)
3. **Test edge cases**: Empty variable names, malformed curly braces, etc.
4. **Test cache invalidation**: Change game state variable, verify cache clears
5. **Test multiple NPCs**: Create 100 NPCs with same variable sprite, verify single resolution
6. **Test resolution failure**: Test behavior when variable not set or resolution fails

