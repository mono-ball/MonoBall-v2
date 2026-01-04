# Elevation-Based Rendering Performance Analysis

**Date**: 2025-01-XX  
**Purpose**: Performance analysis of the elevation-based rendering design, identifying bottlenecks and optimization opportunities.

---

## Executive Summary

The elevation-based rendering design introduces several performance considerations compared to the current layer-based approach. Key areas for optimization:

1. **Sorting Performance**: Sorting all renderables (tiles + sprites) instead of separate sorts
2. **Collection Merging**: Combining separate collections into single list
3. **Render Target Usage**: Multi-target rendering (Option D) has significant performance cost
4. **Culling Efficiency**: Culling before vs after sorting
5. **Memory Allocations**: RenderableItem structure and collections

---

## Current Performance Characteristics

### Current System (Layer-Based)

**MapRendererSystem**:
- Queries tile chunks: ~100-500 entities
- Sorts by `LayerIndex` + `LayerId`: O(n log n) where n = 100-500
- Renders in sorted order
- Culls before sorting (efficient)

**SpriteRendererSystem**:
- Queries sprites: ~10-100 entities
- Sorts by `RenderOrder`: O(n log n) where n = 10-100
- Renders in sorted order
- Culls before sorting (efficient)

**Total Sorting Cost**: 
- Separate sorts: O(500 log 500) + O(100 log 100) ≈ ~4500 + ~665 = ~5165 comparisons
- Independent, can be parallelized (but aren't currently)

### Proposed System (Elevation-Based)

**ElevationRendererSystem**:
- Queries tile chunks + sprites: ~110-600 entities
- Single sort by elevation + Y: O(n log n) where n = 600
- Total: O(600 log 600) ≈ ~5400 comparisons

**Analysis**: Sorting performance is similar or slightly better (single sort vs two separate sorts). However, other factors introduce overhead.

---

## Performance Issues Identified

### 🔴 CRITICAL: Multi-Target Rendering (Option D) Performance Cost

**Issue**: Option D (Multi-Target with Effect Layers) requires:
1. Render tiles to render target A
2. Render sprites to render target B
3. Composite both targets
4. Apply post-processing shader stack

**Performance Impact**:
- **3x render target operations** (A, B, composite) vs 1x (single target)
- **2x geometry rendering** (tiles once, sprites once, composite once) vs 1x (interleaved)
- **Memory**: 3 render targets vs 1 (significant VRAM usage)
- **Bandwidth**: Writing to 3 render targets + reading for composite

**Estimated Cost**: ~3-5x slower than Option C (Simplified) when effects are active.

**Mitigation**:
- Use Option C (Simplified) when no effect shaders are active
- Cache render targets (already done via RenderTargetManager)
- Consider smaller render targets for effects (if effects don't need full resolution)

**Recommendation**: Always prefer Option C when effects aren't needed. Option D should be opt-in/automatic only when effects are detected.

---

### 🟡 MODERATE: Single List vs Separate Lists

**Current Design**: Single `_renderableList` containing all renderables (tiles + sprites)

**Performance Considerations**:

**Pros**:
- Single sort operation
- Simpler code

**Cons**:
- Larger list (600 items vs 500 + 100)
- More memory per item (RenderableItem struct with nullable tuples)
- Cache locality: Mixing tile and sprite data may reduce cache efficiency

**Alternative: Separate Lists with Merge**

```csharp
private readonly List<TileRenderable> _tileList = new();
private readonly List<SpriteRenderable> _spriteList = new();

// Sort each separately (better cache locality)
_tileList.Sort(CompareTileRenderables);
_spriteList.Sort(CompareSpriteRenderables);

// Merge during rendering (no additional allocation)
RenderMerged(_tileList, _spriteList);
```

**Benefits**:
- Better cache locality (tiles together, sprites together)
- Smaller structs (no nullable tuples for unused types)
- Separate sorts can use type-specific comparisons (potentially faster)

**Cost**: Merge logic during rendering (minimal - just iterate two lists)

**Recommendation**: Consider separate lists if profiling shows cache misses. Start with single list (simpler), optimize if needed.

---

### 🟡 MODERATE: RenderableItem Structure Memory Usage

**Current Design**:
```csharp
private struct RenderableItem
{
    public Entity Entity;
    public byte Elevation;
    public float YPosition;
    public RenderableType Type;
    public (TileChunkComponent, TileDataComponent, PositionComponent, RenderableComponent)? TileChunk;
    public (SpriteComponent, PositionComponent, RenderableComponent)? Sprite;
}
```

**Memory Per Item**: 
- Entity: 4 bytes (struct)
- Elevation: 1 byte
- YPosition: 4 bytes
- Type: 1 byte (enum)
- TileChunk tuple: ~80 bytes (4 components, nullable adds 8 bytes overhead)
- Sprite tuple: ~60 bytes (3 components, nullable adds 8 bytes overhead)
- **Total**: ~158 bytes per item (but only one tuple is used)

**Issue**: Only one tuple is used per item, but struct contains both (wasted space).

**Optimization**: Separate lists (see above) eliminates this waste.

**Alternative**: Use smaller structs, store only what's needed:
```csharp
private struct RenderableItem
{
    public Entity Entity;
    public byte Elevation;
    public float YPosition;
    public RenderableType Type;
    public int Index; // Index into type-specific list (tiles or sprites)
}
```

**Cost**: Requires maintaining separate collections anyway (complexity trade-off).

**Recommendation**: Accept current structure for simplicity. Memory overhead is ~80 bytes/item * 600 items = ~48KB (negligible for modern systems).

---

### 🟡 MODERATE: Culling Before vs After Sorting

**Current Design**: Culls during collection (before sorting)

**Performance**: ✅ Optimal - only sorted items are rendered

**Alternative (worse)**: Collect all, sort all, then cull - would waste sorting time on off-screen items.

**Recommendation**: Keep culling before sorting (current design is correct).

---

### 🟢 MINOR: Sorting Comparison Function

**Current Design**:
```csharp
_renderableList.Sort((a, b) =>
{
    var elevCompare = a.Elevation.CompareTo(b.Elevation);
    if (elevCompare != 0)
        return elevCompare;
    return a.YPosition.CompareTo(b.YPosition);
});
```

**Performance**: Good - early exit when elevations differ (most cases).

**Optimization**: For elevation-only sorting (no Y sorting), could use:
```csharp
_renderableList.Sort((a, b) => a.Elevation.CompareTo(b.Elevation));
```

But Y-sorting is needed for proper overlap, so current approach is correct.

**Alternative: Pre-calculate Sort Key**
```csharp
// Pre-calculate: sortKey = (elevation * 10000) + yPosition
// Then sort by sortKey (single comparison)
_renderableList.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
```

**Benefit**: Single comparison per item
**Cost**: Additional memory (4 bytes per item for SortKey)

**Analysis**: Current tuple comparison is already optimized (elevation comparison is fast, early exit is common). Pre-calculated key may be slightly faster but adds complexity.

**Recommendation**: Keep current approach unless profiling shows sorting is a bottleneck (unlikely for 600 items).

---

### 🟡 MODERATE: Y Position Calculation

**Current Design**: Calculate Y position during collection

**Performance**: Good - calculated once, stored in RenderableItem

**Potential Issue**: Y position calculation requires:
- Tiles: Access to tileset definition for tile height
- Sprites: Access to sprite definition for frame height

**Optimization Opportunities**:
1. **Cache tile height**: Get from camera (already available), avoid tileset lookup
2. **Cache sprite definitions**: Already cached in ResourceManager
3. **Pre-calculate**: If sprite/tile dimensions don't change, could cache Y positions (complex, likely not worth it)

**Recommendation**: Current approach is fine. Y position calculation is O(1) per entity, done once per frame.

---

### 🟢 MINOR: Query Performance

**Current Design**: Two separate queries (tile chunks, sprites)

**Performance**: ✅ Optimal - queries are efficient (Arch ECS optimized)

**Alternative**: Single query with optional components - would be slower (more complex query).

**Recommendation**: Keep separate queries (current design is correct).

---

### 🔴 CRITICAL: SpriteBatch State Changes

**Current Design (Option C - Simplified)**:
- Single SpriteBatch.Begin/End session
- Per-entity shaders handled by helpers (may cause batch breaks)

**Performance Issue**: If helpers change shaders frequently, SpriteBatch restarts are expensive.

**Current Systems**: 
- MapRendererSystem: Uses Immediate mode, single shader (tile layer shader)
- SpriteRendererSystem: Batches by shader to minimize state changes

**ElevationRendererSystem Issue**: Rendering in elevation order may intermix entities with different shaders, causing batch breaks.

**Example Problem**:
1. Render tile chunk (no per-entity shader)
2. Render sprite (per-entity shader A)
3. Render tile chunk (no per-entity shader) ← SpriteBatch restart!
4. Render sprite (per-entity shader B) ← SpriteBatch restart!

**Current SpriteRendererSystem Solution**: Pre-sorts sprites by shader to batch them together.

**ElevationRendererSystem Problem**: Can't pre-sort by shader (would break elevation ordering).

**Solutions**:

**Option 1: Accept Batch Breaks** (Simplest)
- Render in elevation order, restart SpriteBatch when shader changes
- Performance cost: Moderate (SpriteBatch.Begin/End is expensive)
- Simplicity: High

**Option 2: Multi-Pass Rendering** (Complex)
- Pass 1: Render all entities with no per-entity shader (sorted by elevation)
- Pass 2: Render all entities with per-entity shader A (sorted by elevation)
- Pass 3: Render all entities with per-entity shader B (sorted by elevation)
- Composite by elevation (requires depth buffer or manual sorting)
- Performance: Better (fewer batch breaks)
- Complexity: High (depth compositing is complex)

**Option 3: Group by Shader, Then Sort by Elevation Within Groups**
- Group entities by shader
- Sort each group by elevation
- Render groups in shader order, entities in elevation order within group
- Problem: Elevation ordering lost across shader groups
- Performance: Good (minimal batch breaks)
- Correctness: Broken (elevation ordering not maintained)

**Option 4: Two-Phase Rendering** (Recommended)
- Phase 1: Collect and sort by elevation
- Phase 2: Render with shader batching:
  - Group consecutive items with same shader
  - Render group in elevation order
  - Move to next group (may break elevation order slightly, but minimal)
- Performance: Good (reduces batch breaks)
- Correctness: Nearly correct (elevation order maintained within shader groups)

**Recommendation**: Option 1 for initial implementation (accept batch breaks). If profiling shows SpriteBatch overhead is significant, implement Option 4.

---

### 🟡 MODERATE: Helper Renderer Overhead

**Current Design**: Extracted rendering logic to helper classes (`TileChunkRenderer`, `SpriteRenderer`)

**Performance Consideration**: Method calls add overhead (minimal in C#, but still present).

**Analysis**: 
- Helper methods are called once per renderable item
- C# method call overhead: ~1-2 nanoseconds (negligible)
- Benefits (code organization, testability) outweigh costs

**Recommendation**: Keep helpers (overhead is negligible, benefits are significant).

---

### 🟢 MINOR: Render Target Management

**Current Design**: Uses RenderTargetManager (caches render targets)

**Performance**: ✅ Good - render targets are reused (no allocation per frame)

**Recommendation**: Current approach is optimal.

---

## Performance Optimization Recommendations

### High Priority

1. **Avoid Option D When Not Needed**
   - Always prefer Option C (Simplified) when no effect shaders are active
   - Option D should only be used when effects are detected
   - **Impact**: 3-5x performance improvement when effects aren't needed

2. **Accept SpriteBatch Batch Breaks Initially**
   - Render in elevation order, restart SpriteBatch on shader change
   - Profile to measure actual impact
   - **Impact**: Simpler code, acceptable performance for most cases

### Medium Priority

3. **Consider Separate Lists for Cache Locality** (Future Optimization)
   - If profiling shows cache misses, split into `_tileList` and `_spriteList`
   - Sort separately, merge during rendering
   - **Impact**: Better cache performance, but adds complexity

4. **Optimize Shader Batch Breaking** (If Needed)
   - If profiling shows SpriteBatch overhead is significant, implement Option 4 (two-phase rendering)
   - Group consecutive items with same shader, render groups
   - **Impact**: Fewer batch breaks, maintains elevation order

### Low Priority

5. **Pre-calculate Sort Keys** (If Sorting is Bottleneck)
   - Only if profiling shows sorting is a bottleneck (unlikely for 600 items)
   - Pre-calculate `sortKey = (elevation * 10000) + yPosition`
   - **Impact**: Slightly faster sorting, but adds memory/complexity

---

## Performance Benchmarks (Estimated)

### Current System (Layer-Based)
- Tile chunk sorting: ~0.1ms (500 items)
- Sprite sorting: ~0.01ms (100 items)
- Total sorting: ~0.11ms
- Rendering: Variable (depends on visible items)

### Proposed System (Elevation-Based, Option C)
- Combined sorting: ~0.12ms (600 items)
- Rendering: Similar to current (same geometry, just sorted differently)
- **Total overhead**: ~0.01ms (negligible)

### Proposed System (Elevation-Based, Option D)
- Sorting: ~0.12ms (same as Option C)
- Render targets: 3x operations = ~3x cost
- Rendering: 2x geometry passes = ~2x cost
- Composite: Additional pass = +1x cost
- **Total overhead**: ~5-6x compared to Option C

**Conclusion**: Option C has negligible performance impact. Option D has significant cost (should only be used when effects are needed).

---

## Memory Usage Analysis

### Current System
- MapRendererSystem: ~500 chunks * 40 bytes = 20KB
- SpriteRendererSystem: ~100 sprites * 40 bytes = 4KB
- **Total**: ~24KB

### Proposed System (Option C)
- ElevationRendererSystem: ~600 renderables * 80 bytes = 48KB
- **Overhead**: +24KB (doubled, but still negligible)

### Proposed System (Option D)
- ElevationRendererSystem: ~600 renderables * 80 bytes = 48KB
- Render targets: 3 * (screen width * screen height * 4 bytes) = ~12MB @ 1920x1080
- **Overhead**: Significant VRAM usage (but render targets are shared/reused)

---

## Hot Path Analysis

### Hot Path: Render() Method

**Operations per frame** (Option C):
1. Clear collection: O(1)
2. Query tile chunks: O(visible chunks) ~100-500
3. Query sprites: O(visible sprites) ~10-100
4. Sort: O(n log n) where n = 600
5. Render: O(n) where n = 600

**Optimization Opportunities**:
- ✅ Collections reused (no allocation)
- ✅ Queries cached (QueryDescription)
- ✅ Culling before sorting (optimal)
- ⚠️ Sorting could be optimized (but 600 items is fast)
- ⚠️ SpriteBatch batch breaks (acceptable for initial implementation)

---

## Performance Testing Recommendations

1. **Profile Sorting Performance**
   - Measure sort time for 600 items
   - Compare single list vs separate lists
   - Verify sorting is not a bottleneck

2. **Profile SpriteBatch Overhead**
   - Measure SpriteBatch.Begin/End calls per frame
   - Compare elevation-based (mixed shaders) vs current (batched shaders)
   - Determine if batch break optimization is needed

3. **Profile Render Target Usage**
   - Measure Option D (multi-target) vs Option C (single target)
   - Verify 3-5x performance difference
   - Test with effects enabled vs disabled

4. **Profile Memory Allocations**
   - Verify no allocations in hot path (collections reused)
   - Check RenderableItem struct memory usage
   - Verify render targets are cached (not allocated per frame)

---

## Conclusion

The elevation-based rendering design has acceptable performance characteristics:

- **Option C (Simplified)**: Negligible performance impact (~0.01ms sorting overhead)
- **Option D (Multi-Target)**: Significant performance cost (3-5x slower), but necessary for effects

**Key Recommendations**:
1. Always prefer Option C when effects aren't needed
2. Accept SpriteBatch batch breaks initially (simpler code)
3. Profile to identify actual bottlenecks (don't optimize prematurely)
4. Consider separate lists if cache misses are significant (future optimization)

**Performance is acceptable for the target use case** (2D sprite-based game with ~600 renderable entities). The design prioritizes correctness and code simplicity over micro-optimizations.
