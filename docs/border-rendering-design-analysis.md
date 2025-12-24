# Border Rendering Design Analysis

## Critical Issues Found

### 1. Architecture Issues

#### ❌ **Missing Elevation System**
**Problem**: The design mentions "default elevation" and "overhead elevation" for rendering order, but the current codebase uses `SpriteSortMode.Deferred` which doesn't support depth sorting.

**Current System**:
- `MapRendererSystem` uses `SpriteSortMode.Deferred` (no depth sorting)
- No elevation component or depth calculation system
- Rendering order is determined by layer index and layer ID sorting

**Impact**: 
- Cannot render bottom layer at "default elevation" and top layer at "overhead elevation"
- Border tiles will render in the same pass as regular tiles, potentially causing z-fighting
- Top layer borders may render behind sprites instead of in front

**Solution Options**:
1. **Option A (Recommended)**: Render borders in two passes:
   - Bottom layer: Render with `MapRendererSystem` (same pass as map tiles)
   - Top layer: Render after `SpriteRendererSystem` (separate pass after sprites)
   
2. **Option B**: Add depth sorting support to rendering system (major refactor)

**Recommendation**: Use Option A - simpler and aligns with current architecture.

#### ✅ **Player Map Detection Service**
**Status**: Service exists (`IPlayerMapService`)

**Solution**: 
- Use `IPlayerMapService.GetPlayerCurrentMapId()` to get player's current map
- Service handles player position querying and map bounds checking
- Returns `string?` (null if player not found or not in any map)

#### ❌ **Rendering Integration Not Specified**
**Problem**: Design says "call from Game.Draw()" but current system uses `SceneRendererSystem` which calls `MapRendererSystem` and `SpriteRendererSystem`.

**Current Flow**:
```
Game.Draw() 
→ SceneRendererSystem.Render() 
→ RenderGameScene() 
→ MapRendererSystem.Render() 
→ SpriteRendererSystem.Render()
```

**Solution**: 
- Integrate `MapBorderRendererSystem` into `SceneRendererSystem.RenderGameScene()`
- Call after `MapRendererSystem` for bottom layer
- Call after `SpriteRendererSystem` for top layer

#### ❌ **Map Unloading Not Handled**
**Problem**: Design doesn't mention what happens when maps are unloaded.

**Current System**: `MapLoaderSystem` has `UnloadMap()` method that removes map entities.

**Solution**: 
- Border components are automatically removed when map entity is destroyed (Arch ECS handles this)
- No explicit cleanup needed, but should be documented

### 2. Arch ECS Issues

#### ⚠️ **Arrays in Struct Components**
**Problem**: `MapBorderComponent` contains arrays (`int[]`, `Rectangle[]`) which are reference types.

**Arch ECS Behavior**:
- Arrays are stored as references, not copied
- If arrays are mutated, all entities sharing the component will see changes
- Need to ensure arrays are initialized and not shared

**Solution**:
- Initialize arrays in component creation (in `MapLoaderSystem`)
- Never mutate arrays after component creation
- Consider using `ReadOnlyMemory<int>` or `ImmutableArray<int>` for safety (but adds complexity)

**Current Pattern**: Other components use arrays (e.g., `TileDataComponent.TileIndices`), so this is acceptable but needs careful initialization.

#### ✅ **Query Description Caching**
**Status**: Correct - query description is cached in constructor as per Arch ECS best practices.

#### ⚠️ **Update() vs Render() Method**
**Problem**: Design mentions "Cache Phase (in Update() or separate method)" but rendering systems don't use `Update()`.

**Current Pattern**:
- `MapRendererSystem` has `Render(GameTime)` method
- `SpriteRendererSystem` has `Render(GameTime)` method
- No `Update()` method in rendering systems

**Solution**: 
- Remove mention of `Update()` method
- Cache data in `Render()` method at the start
- Or create separate `CacheFrameData()` method called before rendering

### 3. Performance Issues

#### ⚠️ **Per-Frame Caching Strategy**
**Problem**: Design mentions "cache map bounds per frame" but doesn't specify:
- When to clear cache
- How to handle map loading/unloading during frame
- Cache invalidation strategy

**Solution**:
- Cache map bounds at start of `Render()` method
- Clear cache each frame (simple and safe)
- Or use dirty flag to only recache when maps change

#### ⚠️ **Querying All Maps Every Frame**
**Problem**: Design says "Query all maps with MapBorderComponent" every frame, even if camera hasn't moved.

**Optimization**:
- Only query if camera bounds changed
- Cache player map ID and only recalculate when player moves
- Early exit if camera is entirely within map bounds

#### ⚠️ **Array Initialization Overhead**
**Problem**: Creating arrays for `BottomLayerGids`, `TopLayerGids`, `BottomSourceRects`, `TopSourceRects` during map loading.

**Impact**: Minor - only happens once per map load, but should be optimized.

**Solution**: 
- Pre-allocate arrays with fixed size (4 elements)
- Use `ArrayPool<int>` and `ArrayPool<Rectangle>` for reuse (overkill for 4 elements)

### 4. Design Inconsistencies

#### ⚠️ **Component Naming**
**Issue**: Component is named `MapBorderComponent` but other components don't have "Component" suffix in their type name (e.g., `MapComponent`, `PositionComponent`).

**Current Pattern**: Components DO have "Component" suffix, so this is correct.

#### ⚠️ **Source Rectangle Calculation**
**Problem**: Design says "pre-calculate source rectangles" but doesn't specify:
- What happens if tileset definition changes
- How to handle tileset reloading
- Error handling if source rectangle calculation fails

**Solution**: 
- Calculate during map loading (tilesets are loaded before maps)
- If calculation fails, log warning and skip border component creation
- Source rectangles are immutable after creation

#### ⚠️ **Border Tile GID vs Local Tile ID**
**Problem**: Design doesn't clarify if border tile IDs in JSON are:
- Global GIDs (with firstGid offset)
- Local tile IDs (within tileset)

**From JSON Example**: `"bottomLayer": [1, 3, 5, 7]` - these appear to be local tile IDs.

**Solution**: 
- Clarify in design: border tile IDs are local tile IDs (1-based within tileset)
- Use `firstGid` from tileset reference to convert to global GID
- Or use tileset loader's `CalculateSourceRectangle()` which handles this

### 5. Missing Design Elements

#### ❌ **Camera Bounds Calculation**
**Problem**: Design mentions "get camera bounds in tile coordinates" but doesn't specify how.

**Current System**: `CameraComponent.GetTileViewBounds()` exists and returns `Rectangle` in tile coordinates.

**Solution**: Use existing `GetTileViewBounds()` method.

#### ❌ **Map Bounds Calculation**
**Problem**: Design mentions "cache map bounds" but doesn't specify format.

**Current System**: 
- `MapComponent` has `Width`, `Height`, `TileWidth`, `TileHeight`
- `PositionComponent` has `Position` (pixel coordinates)
- Need to calculate tile bounds from these

**Solution**: 
```csharp
int mapOriginTileX = (int)(mapPosition.Position.X / map.TileWidth);
int mapOriginTileY = (int)(mapPosition.Position.Y / map.TileHeight);
int mapRightTile = mapOriginTileX + map.Width;
int mapBottomTile = mapOriginTileY + map.Height;
```

#### ❌ **Tile Position Calculation**
**Problem**: Design mentions "calculate relative position to map origin" but doesn't specify coordinate system.

**Solution**: 
- Use tile coordinates (not pixel coordinates) for border tile index calculation
- Convert tile coordinates to pixel coordinates for rendering

#### ❌ **Border Render Margin**
**Problem**: Design mentions "render tiles in camera view (with margin)" but doesn't specify margin size.

**From oldmonoball**: Uses `CameraConstants.BorderRenderMarginTiles` (typically 1-2 tiles).

**Solution**: Use 1 tile margin (same as `MapRendererSystem` uses for chunk expansion).

## Recommended Design Changes

### 1. Remove Elevation References
Replace "default elevation" and "overhead elevation" with:
- Bottom layer: Render in same pass as `MapRendererSystem`
- Top layer: Render in separate pass after `SpriteRendererSystem`

### 2. Clarify Rendering Integration
Specify exact integration point:
```csharp
// In SceneRendererSystem.RenderGameScene():
_mapRendererSystem.Render(gameTime);  // Renders map tiles + border bottom layer

_spriteRendererSystem.Render(gameTime);  // Renders sprites

_mapBorderRendererSystem.RenderTopLayer(gameTime);  // Renders border top layer
```

### 3. Add Player Map Detection
Either:
- Add `GetPlayerCurrentMapId()` to `ICameraService`
- Or query directly in `MapBorderRendererSystem` using existing pattern

### 4. Specify Caching Strategy
- Cache map bounds at start of `Render()` method
- Clear cache each frame (simple and safe)
- Cache player map ID and only recalculate when player position changes significantly

### 5. Clarify Border Tile ID Format
Document that border tile IDs in JSON are local tile IDs (1-based), and conversion to global GID happens during component creation.

### 6. Add Error Handling
Specify what happens when:
- Tileset not found during border component creation
- Source rectangle calculation fails
- Border data is invalid (wrong array length, null values)

### 7. Add Map Unloading Documentation
Document that border components are automatically removed when map entities are destroyed (Arch ECS handles this).

## Performance Recommendations

1. **Early Exit Optimization**: Check if camera is entirely within map bounds before any rendering
2. **Player Map Caching**: Cache player's current map ID and only recalculate when player moves significantly
3. **Bounds Caching**: Cache map bounds in tile coordinates (avoid divisions per frame)
4. **Query Optimization**: Only query maps with border components (already handled by query description)
5. **Render Culling**: Only render tiles in camera view + small margin (1 tile)

## Architecture Alignment

The design should align with:
- ✅ Current rendering system architecture (SpriteBatch, deferred sorting)
- ✅ Current component patterns (struct components, cached queries)
- ✅ Current system patterns (Render() methods, SetSpriteBatch() pattern)
- ❌ Elevation system (doesn't exist - needs design change)
- ✅ Map loading/unloading patterns
- ✅ Player tracking patterns (query-based)

