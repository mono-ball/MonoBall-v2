# Multi-Tileset Support Implementation - Architecture Analysis

## Overview

This document analyzes the implementation of multi-tileset support in maps, identifying architecture issues, SOLID/DRY violations, ECS/event issues, code smells, and performance concerns.

## Changes Summary

1. **MapDefinition.cs**: Renamed `TilesetRefs` to `Tilesets` to match JSON property name
2. **MapLoaderSystem.cs**: 
   - Added `_mapTilesetRefs` dictionary to store tileset references per map
   - Added `ResolveTilesetForGid()` helper method
   - Updated chunk creation to resolve tilesets per-tile for animations
3. **MapRendererSystem.cs**:
   - Added `DefinitionRegistry` dependency
   - Added `_mapTilesetRefsCache` dictionary
   - Added `GetTilesetRefsForMap()` and `ResolveTilesetForGid()` methods
   - Updated rendering to resolve tilesets per-tile (both fast and slow paths)
4. **SystemManager.cs**: Updated to pass `DefinitionRegistry` to `MapRendererSystem`

---

## Architecture Issues

### 1. ❌ **DRY Violation: Duplicate ResolveTilesetForGid Methods**

**Issue**: Both `MapLoaderSystem` and `MapRendererSystem` have identical `ResolveTilesetForGid()` methods.

**Location**:
- `MapLoaderSystem.cs:1556-1595`
- `MapRendererSystem.cs:715-754`

**Impact**: 
- Code duplication violates DRY principle
- Bug fixes must be applied in two places
- Logic divergence risk over time

**Recommendation**: Extract to shared utility class:
```csharp
// MonoBall.Core.Maps.Utilities/TilesetResolver.cs
public static class TilesetResolver
{
    public static (string TilesetId, int FirstGid) ResolveTilesetForGid(
        int gid,
        List<TilesetReference> tilesetRefs
    )
    {
        // Shared implementation
    }
}
```

---

### 2. ⚠️ **DRY Violation: Duplicate Tileset Refs Storage**

**Issue**: Both systems store tileset references separately:
- `MapLoaderSystem._mapTilesetRefs`
- `MapRendererSystem._mapTilesetRefsCache`

**Impact**:
- Duplicate data in memory
- Inconsistent caching strategies
- Potential for data divergence

**Recommendation**: 
- Option A: Store tileset refs in `MapComponent` (but it's a struct, would need to change to class)
- Option B: Create shared `MapTilesetService` to manage tileset refs
- Option C: Keep separate but document why (MapLoaderSystem needs it during load, MapRendererSystem needs it during render)

**Current State**: Option C is acceptable since they serve different purposes, but should be documented.

---

### 3. ⚠️ **New Dependency: MapRendererSystem → DefinitionRegistry**

**Issue**: `MapRendererSystem` now depends on `DefinitionRegistry` to look up map definitions.

**Impact**:
- Increased coupling
- MapRendererSystem now needs to know about mod system
- Could be avoided by storing tileset refs in components

**Recommendation**: 
- Consider storing tileset refs in `MapComponent` or a new `MapTilesetsComponent`
- Or create a lightweight `IMapTilesetProvider` interface to abstract the lookup

**Current State**: Acceptable but increases coupling. Consider refactoring if more systems need tileset refs.

---

### 4. ⚠️ **Performance: Per-Tile Tileset Resolution in Hot Path**

**Issue**: `MapRendererSystem.RenderChunk()` resolves tileset for every tile in the rendering loop.

**Impact**:
- O(n) lookup per tile (where n = number of tilesets)
- Called every frame for every visible tile
- Could be optimized by pre-computing tileset per tile or caching

**Current Implementation**:
```csharp
// Called for every tile in hot rendering path
var (resolvedTilesetId, resolvedFirstGid) = ResolveTilesetForGid(gid, tilesetRefs);
```

**Recommendation**:
- Option A: Pre-compute tileset per tile during chunk creation (store in `TileDataComponent` or separate component)
- Option B: Cache resolved tilesets per GID range (but GIDs can be sparse)
- Option C: Optimize `ResolveTilesetForGid` to use binary search instead of linear scan

**Current State**: Acceptable for now, but should be optimized if performance becomes an issue.

---

## SOLID/DRY Issues

### 5. ❌ **Single Responsibility Violation: ResolveTilesetForGid Logic**

**Issue**: `ResolveTilesetForGid` uses `IndexOf()` which is O(n), and the logic is complex.

**Current Implementation**:
```csharp
var currentIndex = tilesetRefs.IndexOf(tilesetRef);
if (currentIndex > 0)
{
    var nextTilesetRef = tilesetRefs[currentIndex - 1];
    // ...
}
```

**Problems**:
- `IndexOf()` is O(n) - inefficient
- Complex nested conditionals
- Could use binary search or simple iteration

**Recommendation**: Simplify logic:
```csharp
// Tilesets are sorted descending, so first match is correct
foreach (var tilesetRef in tilesetRefs)
{
    if (gid >= tilesetRef.FirstGid)
    {
        // Check if there's a higher firstGid tileset
        var nextIndex = tilesetRefs.IndexOf(tilesetRef) - 1;
        if (nextIndex < 0 || gid < tilesetRefs[nextIndex].FirstGid)
            return (tilesetRef.TilesetId, tilesetRef.FirstGid);
    }
}
```

**Better**: Use indexed iteration:
```csharp
for (int i = 0; i < tilesetRefs.Count; i++)
{
    var tilesetRef = tilesetRefs[i];
    if (gid >= tilesetRef.FirstGid)
    {
        // Check next tileset (if exists)
        if (i == 0 || gid < tilesetRefs[i - 1].FirstGid)
            return (tilesetRef.TilesetId, tilesetRef.FirstGid);
    }
}
```

---

### 6. ⚠️ **Code Duplication: Fast Path vs Slow Path**

**Issue**: Both fast path (non-animated) and slow path (animated) have similar tileset resolution code.

**Location**: `MapRendererSystem.cs:496-540` (fast) vs `564-657` (slow)

**Impact**: 
- Duplicate code
- Bug fixes must be applied twice
- Inconsistent behavior risk

**Recommendation**: Extract common tileset resolution logic:
```csharp
private (Texture2D texture, TilesetDefinition definition, Rectangle sourceRect) ResolveTileForRendering(
    int gid,
    List<TilesetReference> tilesetRefs,
    Texture2D defaultTexture,
    TilesetDefinition defaultDefinition
)
{
    var (resolvedTilesetId, resolvedFirstGid) = ResolveTilesetForGid(gid, tilesetRefs);
    // ... load texture/definition if needed
    // ... calculate source rect
    return (texture, definition, sourceRect);
}
```

---

## ECS/Event Issues

### 7. ✅ **No Event Issues**

**Status**: No ECS/event issues identified. The changes don't affect event system.

---

## Code Smells

### 8. ⚠️ **Magic Numbers: No Validation of Tileset Ranges**

**Issue**: No validation that tileset `firstGid` values are valid or don't overlap incorrectly.

**Impact**: 
- Invalid map definitions could cause runtime errors
- Overlapping tileset ranges could cause incorrect resolution

**Recommendation**: Add validation in `MapLoaderSystem.LoadMap()`:
```csharp
// Validate tileset ranges don't overlap incorrectly
if (mapDefinition.Tilesets != null && mapDefinition.Tilesets.Count > 1)
{
    var sorted = mapDefinition.Tilesets.OrderByDescending(t => t.FirstGid).ToList();
    for (int i = 0; i < sorted.Count - 1; i++)
    {
        if (sorted[i].FirstGid <= sorted[i + 1].FirstGid)
        {
            _logger.Warning(
                "Map {MapId} has overlapping tileset firstGids: {Tileset1} ({Gid1}) and {Tileset2} ({Gid2})",
                mapId, sorted[i].TilesetId, sorted[i].FirstGid,
                sorted[i + 1].TilesetId, sorted[i + 1].FirstGid
            );
        }
    }
}
```

---

### 9. ⚠️ **Silent Failures: Exception Swallowing**

**Issue**: Multiple `catch (Exception)` blocks that silently continue without logging.

**Location**: 
- `MapRendererSystem.cs:523` (fast path)
- `MapRendererSystem.cs:652` (slow path)
- `MapRendererSystem.cs:669` (slow path)

**Impact**: 
- Errors are hidden, making debugging difficult
- Could mask real issues (missing tilesets, invalid GIDs)

**Recommendation**: 
- Log warnings for unexpected exceptions
- Only silently skip for known recoverable errors (e.g., invalid GID range)

**Current State**: Acceptable for performance-critical rendering path, but should log at debug level.

---

### 10. ⚠️ **Inefficient IndexOf Usage**

**Issue**: `ResolveTilesetForGid` uses `IndexOf()` which is O(n) in a loop.

**Location**: `MapLoaderSystem.cs:1571`, `MapRendererSystem.cs:730`

**Impact**: 
- O(n²) complexity for tileset resolution
- Called frequently in hot paths

**Recommendation**: Use indexed iteration instead:
```csharp
for (int i = 0; i < tilesetRefs.Count; i++)
{
    var tilesetRef = tilesetRefs[i];
    if (gid >= tilesetRef.FirstGid)
    {
        if (i == 0 || gid < tilesetRefs[i - 1].FirstGid)
            return (tilesetRef.TilesetId, tilesetRef.FirstGid);
    }
}
```

---

### 11. ⚠️ **Missing XML Documentation**

**Issue**: `ResolveTilesetForGid` methods have XML docs, but `GetTilesetRefsForMap` is missing detailed docs.

**Location**: `MapRendererSystem.cs:694-714`

**Recommendation**: Add comprehensive XML documentation explaining caching behavior.

---

## Performance Concerns

### 12. ⚠️ **Repeated Tileset Texture Loading**

**Issue**: `MapRendererSystem` loads tileset textures per-tile if they differ from default.

**Impact**: 
- Multiple `LoadTexture()` calls for same tileset in one chunk
- Texture loading is expensive

**Current Implementation**:
```csharp
if (resolvedTilesetId != data.TilesetId)
{
    tilesetTexture = _resourceManager.LoadTexture(resolvedTilesetId);
    // ...
}
```

**Recommendation**: 
- `ResourceManager.LoadTexture()` already caches textures, so this is fine
- But we're calling it multiple times per chunk - could cache per-chunk

**Current State**: Acceptable since `ResourceManager` caches textures.

---

### 13. ⚠️ **Map Definition Lookup Per Chunk**

**Issue**: `GetTilesetRefsForMap()` looks up map definition from registry (cached after first call).

**Impact**: 
- First call per map does registry lookup
- Subsequent calls use cache (good)

**Current State**: Acceptable - caching mitigates the issue.

---

## Recommendations Summary

### Critical (Must Fix)

1. **Extract `ResolveTilesetForGid` to shared utility** - DRY violation
2. **Fix `IndexOf()` inefficiency** - Performance issue

### Important (Should Fix)

3. **Extract common tileset resolution logic** - Reduce duplication between fast/slow paths
4. **Add tileset range validation** - Prevent invalid map definitions
5. **Add debug logging for exceptions** - Improve debuggability

### Nice to Have (Consider)

6. **Consider storing tileset refs in component** - Reduce DefinitionRegistry dependency
7. **Optimize per-tile resolution** - Pre-compute or cache if performance becomes issue
8. **Add comprehensive XML docs** - Improve maintainability

---

## Conclusion

The implementation successfully adds multi-tileset support, but has several DRY violations and performance concerns:

- **DRY Violations**: Duplicate `ResolveTilesetForGid` methods and tileset refs storage
- **Performance**: Per-tile resolution in hot path, inefficient `IndexOf()` usage
- **Code Quality**: Missing validation, silent exception handling

**Priority**: Extract shared utility for `ResolveTilesetForGid` and optimize `IndexOf()` usage first, then address other issues as needed.
