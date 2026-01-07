# Elevation-Based Rendering System Design

**Date**: 2025-01-XX  
**Purpose**: Design document for transitioning from sequential layer-based rendering to elevation-based rendering system, inspired by Pokemon Emerald's elevation model.

---

## Executive Summary

Currently, our rendering system uses sequential layers: maps render first (sorted by `LayerIndex`/`LayerId`), followed by sprites (sorted by `RenderOrder`), then borders. This document designs a transition to elevation-based rendering where all renderable entities (tiles, sprites, borders) are sorted by elevation value first, then by Y position within each elevation level.

**Key Changes:**
- All renderables sorted by elevation (0-15), then Y position
- Unified rendering pass instead of separate systems for maps/sprites/borders
- Elevation data stored per-entity and per-tile chunk
- Support for multi-level maps (bridges, overhead structures, multi-floor buildings)

---

## Current System Analysis

### Current Rendering Flow

**GameSceneSystem.RenderScene()** calls systems in sequence:
1. `MapRendererSystem.Render()` - Renders all tile chunks, sorted by `LayerIndex` then `LayerId`
2. `MapBorderRendererSystem.Render()` - Renders border bottom layer
3. `SpriteRendererSystem.Render()` - Renders sprites, sorted by `RenderOrder`
4. `MapBorderRendererSystem.RenderTopLayer()` - Renders border top layer

### Current Data Structures

#### Tile Chunks
- **TileChunkComponent**: Contains `LayerId` (string) and `LayerIndex` (int) for rendering order
- **TileDataComponent**: Contains tile GID data, no elevation information
- **MapLayer**: Contains layer metadata but no elevation field (elevation exists in `LayerData` in Porycon3 conversion)

#### Sprites
- **RenderableComponent**: Contains `RenderOrder` (int) for rendering order
- **NpcComponent**: Contains `Elevation` (int) property (0-15) - Note: Currently `int`, should be `byte` for consistency
- **PlayerComponent**: No elevation property (stored in constants or needs to be added)

#### Borders
- **MapBorderRendererSystem**: Renders borders as direct tile draws (not entities)
  - Bottom layer rendered between maps and sprites
  - Top layer rendered after sprites
  - Borders are procedural (2x2 tiling pattern), not stored as entities
  - Elevation is implicit (bottom = 3, top = 9) based on render order

### Current Limitations

1. **Sequential Layers**: Maps always render before sprites, even if a sprite should appear behind a map layer
2. **No Per-Tile Elevation**: Tile chunks have layer-based sorting, not per-tile elevation
3. **Inconsistent Elevation Storage**: NPCs have elevation, players don't (inconsistent data model)
4. **Separate Render Passes**: Maps, sprites, and borders render separately, preventing true elevation-based sorting
5. **Border Elevation Hardcoded**: Border elevations are fixed (3 and 9) instead of configurable

---

## Design Goals

1. **Unified Elevation System**: All entity-based renderables (tiles, sprites) use the same elevation-based sorting. Borders remain procedural and are rendered at fixed elevations.
2. **Per-Tile Elevation Support**: Support elevation stored per-tile (from map.bin data) for future enhancement
3. **Backward Compatibility**: Support maps that only have layer-based elevation (current system) while allowing per-tile elevation (future)
4. **Performance**: Maintain efficient rendering with minimal overhead from elevation sorting
5. **Consistent Data Model**: All entities use `ElevationComponent` for elevation storage

---

## Proposed Architecture

### Component Changes

#### 1. ElevationComponent (New)

Create a new component for storing elevation on all entities:

```csharp
namespace MonoBall.Core.ECS.Components;

/// <summary>
/// Component that stores elevation level for an entity (0-15).
/// Used for rendering order and collision detection.
/// </summary>
public struct ElevationComponent
{
    /// <summary>
    /// The elevation level (0-15).
    /// Higher values render on top of lower values.
    /// </summary>
    public byte Elevation { get; set; }
    
    /// <summary>
    /// Default elevation for most ground tiles and objects.
    /// </summary>
    public const byte Default = 3;
    
    /// <summary>
    /// Ground level (water, pits, lower terrain).
    /// </summary>
    public const byte Ground = 0;
    
    /// <summary>
    /// Bridge level (walkways over water/ground).
    /// </summary>
    public const byte Bridge = 6;
    
    /// <summary>
    /// Overhead level (tall trees, building roofs).
    /// </summary>
    public const byte Overhead = 9;
    
    /// <summary>
    /// Maximum elevation level.
    /// </summary>
    public const byte Max = 15;
}
```

**Location**: `MonoBall.Core/ECS/Components/ElevationComponent.cs`

**Migration Strategy**:
- NPCs: Move `NpcComponent.Elevation` → `ElevationComponent` on NPC entities
- Players: Add `ElevationComponent` (default value 3 or from constants)
- Tile Chunks: Add `ElevationComponent` with layer's elevation value
- Borders: Keep borders as procedural renders (not entities) - rendered at fixed elevation passes (see Border Rendering section)

#### 2. TileChunkComponent (Unchanged)

**Note**: `TileChunkComponent` does NOT need an elevation field. Elevation is stored in `ElevationComponent` on the chunk entity. This maintains a single source of truth and avoids data duplication (DRY principle).

**Rationale**: 
- Elevation stored in `ElevationComponent` only (single source of truth)
- Per cursorrules: "NO BACKWARD COMPATIBILITY - Refactor APIs freely, break existing code if needed"
- For per-tile elevation (future), individual tile entities would have their own `ElevationComponent`

#### 3. RenderableComponent (Unchanged)

Keep `RenderableComponent` but deprecate `RenderOrder` for rendering (still useful for other purposes like UI ordering).

**Note**: `RenderOrder` may still be used for non-elevation-based rendering contexts (UI, menus, etc.), but elevation-based rendering will use `ElevationComponent` + Y position.

---

### System Changes

#### 1. ElevationRendererSystem (New)

Create a new unified rendering system that coordinates elevation-based rendering:

```csharp
namespace MonoBall.Core.ECS.Systems;

/// <summary>
/// Unified rendering system that renders all entities by elevation, then Y position.
/// Coordinates rendering by collecting and sorting renderables, then delegating to helper renderers.
/// </summary>
public class ElevationRendererSystem : BaseSystem<World, float>
{
    /// <summary>
    /// Elevation at which bottom border layer is rendered.
    /// </summary>
    public const byte BorderBottomElevation = 3;

    /// <summary>
    /// Elevation at which top border layer is rendered.
    /// </summary>
    public const byte BorderTopElevation = 9;

    /// <summary>
    /// Default elevation for entities without explicit elevation.
    /// </summary>
    public const byte DefaultElevation = 3;

    // Queries for different renderable types
    private readonly QueryDescription _tileChunkQuery;
    private readonly QueryDescription _spriteQuery;
    
    // Reusable collections for sorting
    private readonly List<RenderableItem> _renderableList = new();

    // Cached comparer to avoid lambda allocation on every frame
    private static readonly Comparison<RenderableItem> _renderableComparer = (a, b) =>
    {
        var elevCompare = a.Elevation.CompareTo(b.Elevation);
        if (elevCompare != 0)
            return elevCompare;
        return a.YPosition.CompareTo(b.YPosition);
    };

    // Helper renderers (extracted rendering logic) - injected via interfaces for testability
    private readonly ITileChunkRenderer _tileRenderer;
    private readonly ISpriteRenderer _spriteRenderer;
    private readonly MapBorderRendererSystem? _mapBorderRendererSystem;
    
    // Dependencies (injected)
    private readonly World _world;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly ICameraService _cameraService;
    private readonly IResourceManager _resourceManager;
    private readonly DefinitionRegistry _definitionRegistry;
    private readonly ILogger _logger;
    private readonly ShaderManagerSystem? _shaderManagerSystem;
    private readonly ShaderRendererSystem? _shaderRendererSystem;
    private readonly RenderTargetManager? _renderTargetManager;
    private readonly IShaderService? _shaderService;
    private readonly PerformanceStatsSystem? _performanceStatsSystem;

    public ElevationRendererSystem(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        ICameraService cameraService,
        IResourceManager resourceManager,
        DefinitionRegistry definitionRegistry,
        ILogger logger,
        ShaderManagerSystem? shaderManagerSystem = null,
        ShaderRendererSystem? shaderRendererSystem = null,
        RenderTargetManager? renderTargetManager = null,
        IShaderService? shaderService = null,
        PerformanceStatsSystem? performanceStatsSystem = null
    ) : base(world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _definitionRegistry = definitionRegistry ?? throw new ArgumentNullException(nameof(definitionRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _shaderManagerSystem = shaderManagerSystem;
        _shaderRendererSystem = shaderRendererSystem;
        _renderTargetManager = renderTargetManager;
        _shaderService = shaderService;
        _performanceStatsSystem = performanceStatsSystem;
        
        // Initialize queries in constructor (per .cursorrules)
        _tileChunkQuery = new QueryDescription().WithAll<
            TileChunkComponent,
            TileDataComponent,
            PositionComponent,
            RenderableComponent,
            ElevationComponent
        >();
        
        _spriteQuery = new QueryDescription().WithAll<
            SpriteComponent,
            PositionComponent,
            RenderableComponent,
            ElevationComponent
        >();
        
        // Initialize helper renderers
        _tileRenderer = new TileChunkRenderer(
            _graphicsDevice,
            _resourceManager,
            _definitionRegistry,
            _logger
        );
        
        _spriteRenderer = new SpriteRenderer(
            _graphicsDevice,
            _resourceManager,
            _logger,
            _shaderService
        );
    }
    
    
    /// <summary>
    /// Renders all entities sorted by elevation, then Y position.
    /// </summary>
    public void Render(GameTime gameTime, Entity? sceneEntity = null)
    {
        if (_spriteBatch == null)
            throw new InvalidOperationException("SpriteBatch must be set via Initialize before calling Render");

        // 1. Collect all renderables (tiles, sprites)
        CollectRenderables();

        // 2. Sort by elevation, then Y position (cached comparison to avoid allocation)
        _renderableList.Sort(_renderableComparer);
        
        // 3. Render all in sorted order using single SpriteBatch session
        RenderSortedItems(gameTime, sceneEntity);
    }
    
    private void CollectRenderables()
    {
        _renderableList.Clear();
        
        var activeCamera = _cameraService.GetActiveCamera();
        if (!activeCamera.HasValue)
            return;
        
        var camera = activeCamera.Value;
        var tileViewBounds = camera.GetTileViewBounds();
        
        // Collect tile chunks
        World.Query(in _tileChunkQuery, (
            Entity entity,
            ref TileChunkComponent chunk,
            ref TileDataComponent data,
            ref PositionComponent pos,
            ref RenderableComponent render,
            ref ElevationComponent elev
        ) =>
        {
            if (!render.IsVisible)
                return;
            
            // Cull off-screen chunks (reuse logic from MapRendererSystem)
            if (IsChunkInView(chunk, pos, camera))
            {
                _renderableList.Add(new RenderableItem
                {
                    Entity = entity,
                    Elevation = elev.Elevation,
                    YPosition = CalculateTileBottomY(chunk, pos),
                    Type = RenderableType.TileChunk,
                    TileChunk = (chunk, data, pos, render)
                });
            }
        });
        
        // Collect sprites (NPCs and Players)
        World.Query(in _spriteQuery, (
            Entity entity,
            ref SpriteComponent sprite,
            ref PositionComponent pos,
            ref RenderableComponent render,
            ref ElevationComponent elev
        ) =>
        {
            if (!render.IsVisible)
                return;
            
            // Cull off-screen sprites
            if (IsSpriteInView(sprite, pos, camera))
            {
                _renderableList.Add(new RenderableItem
                {
                    Entity = entity,
                    Elevation = elev.Elevation,
                    YPosition = CalculateSpriteBottomY(sprite, pos),
                    Type = RenderableType.Sprite,
                    Sprite = (sprite, pos, render)
                });
            }
        });
    }
    
    private void RenderSortedItems(GameTime gameTime, Entity? sceneEntity)
    {
        // Get camera and setup viewport
        var activeCamera = _cameraService.GetActiveCamera();
        if (!activeCamera.HasValue)
            return;
        
        var camera = activeCamera.Value;
        SetupRenderViewport(camera);
        
        // Get combined layer shader stack (post-processing)
        var shaderStack = _shaderManagerSystem?.GetCombinedLayerShaderStack(sceneEntity);
        var hasPostProcessing = shaderStack != null && shaderStack.Count > 0;
        
        // Implementation Note: Option C (Simplified) is the default rendering path.
        // Option D (Multi-Target) should only be used when effect shaders are detected.
        // For design purposes, showing Option C implementation below.
        
        RenderTarget2D? renderTarget = null;
        Viewport? originalViewport = null;
        
        if (hasPostProcessing && _renderTargetManager != null)
        {
            renderTarget = _renderTargetManager.GetOrCreateRenderTarget();
            if (renderTarget != null)
            {
                originalViewport = _graphicsDevice.Viewport;
                _graphicsDevice.SetRenderTarget(renderTarget);
                _graphicsDevice.Clear(Color.Transparent);
            }
        }
        
        try
        {
            var transform = camera.GetTransformMatrix();
            var savedViewport = _graphicsDevice.Viewport;
            
            // Single SpriteBatch session for interleaved rendering
            _spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                SamplerState.PointClamp,
                null,
                null,
                null, // No shader - per-entity shaders handled by helpers, layer shaders applied post-processing
                transform
            );
            
            // Render all items in sorted order (elevation, then Y position)
            // Also render borders at their elevation passes
            bool renderedBottomBorders = false;
            bool renderedTopBorders = false;

            foreach (var item in _renderableList)
            {
                // Render borders at their elevation passes (within the same SpriteBatch session)
                if (item.Elevation >= BorderBottomElevation && !renderedBottomBorders)
                {
                    _mapBorderRendererSystem?.RenderBottomLayerInBatch(gameTime, _spriteBatch);
                    renderedBottomBorders = true;
                }

                if (item.Elevation >= BorderTopElevation && !renderedTopBorders)
                {
                    _mapBorderRendererSystem?.RenderTopLayerInBatch(gameTime, _spriteBatch);
                    renderedTopBorders = true;
                }
                
                // Render entity using polymorphic dispatch (no switch required)
                item.Render(_spriteBatch, _tileRenderer, _spriteRenderer);
            }
            
            // Render any remaining borders if we haven't reached their elevation passes
            if (!renderedBottomBorders)
                _mapBorderRendererSystem?.RenderBottomLayerInBatch(gameTime, _spriteBatch);
            if (!renderedTopBorders)
                _mapBorderRendererSystem?.RenderTopLayerInBatch(gameTime, _spriteBatch);
            
            _spriteBatch.End();
            _performanceStatsSystem?.IncrementDrawCalls();
            
            // Apply post-processing shader stack if needed
            if (renderTarget != null && hasPostProcessing && shaderStack != null && _shaderRendererSystem != null)
            {
                _graphicsDevice.SetRenderTarget(null);
                if (originalViewport.HasValue)
                    _graphicsDevice.Viewport = originalViewport.Value;
                
                var viewport = _graphicsDevice.Viewport;
                _shaderManagerSystem?.UpdateCombinedLayerScreenSize(viewport.Width, viewport.Height);
                _shaderManagerSystem?.ForceUpdateCombinedLayerParameters();
                
                _shaderRendererSystem.ApplyShaderStack(
                    renderTarget,
                    null,
                    shaderStack,
                    _spriteBatch,
                    _graphicsDevice,
                    _renderTargetManager!
                );
            }
            
            _graphicsDevice.Viewport = savedViewport;
        }
        finally
        {
            if (renderTarget != null)
            {
                _graphicsDevice.SetRenderTarget(null);
                if (originalViewport.HasValue)
                    _graphicsDevice.Viewport = originalViewport.Value;
            }
        }
    }
    
    // Helper methods for culling and Y position calculation
    private bool IsChunkInView(TileChunkComponent chunk, PositionComponent pos, CameraComponent camera) { /* ... */ }
    private bool IsSpriteInView(SpriteComponent sprite, PositionComponent pos, CameraComponent camera) { /* ... */ }
    private float CalculateTileBottomY(TileChunkComponent chunk, PositionComponent pos) { /* ... */ }
    private float CalculateSpriteBottomY(SpriteComponent sprite, PositionComponent pos) { /* ... */ }
    private void SetupRenderViewport(CameraComponent camera) { /* ... */ }
}

// Helper renderer interfaces (for testability and DI)
public interface ITileChunkRenderer
{
    void RenderChunk(
        Entity entity,
        TileChunkComponent chunk,
        TileDataComponent data,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    );
}

public interface ISpriteRenderer
{
    void RenderSprite(
        Entity entity,
        SpriteComponent sprite,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    );
}

// Helper renderer implementations
internal sealed class TileChunkRenderer : ITileChunkRenderer
{
    // Extracted tile chunk rendering logic from MapRendererSystem
    public void RenderChunk(
        Entity entity,
        TileChunkComponent chunk,
        TileDataComponent data,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    ) { /* ... */ }
}

internal sealed class SpriteRenderer : ISpriteRenderer
{
    // Extracted sprite rendering logic from SpriteRendererSystem
    public void RenderSprite(
        Entity entity,
        SpriteComponent sprite,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    ) { /* ... */ }
}
```

**Location**: `MonoBall.Core/ECS/Systems/ElevationRendererSystem.cs`

**Responsibilities**:
- Collect all renderables (tile chunks, sprites) in a single list
- Sort by elevation (primary), then Y position (secondary)
- Coordinate rendering using helper renderers
- Handle shader stacking (combined layer shaders for post-processing)

**Architecture Notes**:
- Uses helper renderer classes (`TileChunkRenderer`, `SpriteRenderer`) to maintain separation of concerns
- Single `SpriteBatch.Begin/End` session enables true interleaved rendering
- Borders integrated into elevation-based rendering (rendered at elevation 3 and 9 passes)
- Per-entity shaders handled by helpers during geometry rendering
- Layer shaders applied as post-processing (combined layer shader stack)

**Performance Considerations**:
- Cache query descriptions in constructor (per .cursorrules)
- Reuse collections (clear and refill each frame) - no allocations in hot path
- Cull off-screen entities before sorting - only sort visible entities
- Option C (Simplified): Single SpriteBatch session for interleaved rendering
- Option D (Multi-Target): Multiple render targets, but only used when effects are needed
- SpriteBatch batch breaks: Acceptable for initial implementation (render in elevation order, restart on shader change)

#### 2. MapRendererSystem (Deprecated)

**Status**: Tile rendering logic extracted to `TileChunkRenderer` helper class.

**Migration**:
- Tile rendering logic moved to `TileChunkRenderer` helper (used by `ElevationRendererSystem`)
- `MapRendererSystem` can be removed after migration
- Per cursorrules: "NO BACKWARD COMPATIBILITY - Refactor APIs freely, break existing code if needed"

#### 3. SpriteRendererSystem (Deprecated)

**Status**: Sprite rendering logic extracted to `SpriteRenderer` helper class.

**Migration**:
- Sprite rendering logic moved to `SpriteRenderer` helper (used by `ElevationRendererSystem`)
- `SpriteRendererSystem` can be removed after migration
- Per cursorrules: "NO BACKWARD COMPATIBILITY - Refactor APIs freely, break existing code if needed"

#### 4. MapBorderRendererSystem (Modified - Integrated into Elevation Rendering)

**Status**: Borders remain procedural (not entities) but are integrated into `ElevationRendererSystem` at fixed elevation passes.

**Rationale**: 
- Borders use a 2x2 tiling pattern and are procedural (not stored as entities)
- Borders have fixed elevations (bottom = 3, top = 9) and don't need elevation-based sorting
- **Option A**: Integrate borders into elevation-based rendering to maintain elevation ordering everywhere
- Borders rendered within the same `SpriteBatch` session as other renderables

**Integration**:
- `ElevationRendererSystem` calls border rendering methods during elevation passes:
  - Bottom border layer rendered before elevation 3 items (elevation 3 pass)
  - Top border layer rendered before elevation 9 items (elevation 9 pass)
- Borders rendered within the same `SpriteBatch.Begin/End` session for proper interleaving

**Implementation Changes**:
- `MapBorderRendererSystem` needs new methods that accept `SpriteBatch` parameter:
  - `RenderBottomLayerInBatch(GameTime gameTime, SpriteBatch spriteBatch)` - Renders bottom layer within existing SpriteBatch session
  - `RenderTopLayerInBatch(GameTime gameTime, SpriteBatch spriteBatch)` - Renders top layer within existing SpriteBatch session
- These methods extract the rendering logic from `RenderBorderLayer()` but skip `SpriteBatch.Begin/End` calls
- Remove existing `Render()` and `RenderTopLayer()` methods after migration is complete

**Note**: This maintains elevation-based rendering everywhere - borders are rendered at their elevation passes within the unified elevation rendering system.

#### 5. GameSceneSystem (Modified)

**Before**:
```csharp
_mapRendererSystem.Render(gameTime, sceneEntity);
_mapBorderRendererSystem.Render(gameTime);
_spriteRendererSystem.Render(gameTime, sceneEntity);
_mapBorderRendererSystem.RenderTopLayer(gameTime);
```

**After**:
```csharp
_elevationRendererSystem.Render(gameTime, sceneEntity);
```

**Changes**:
- Inject `ElevationRendererSystem` instead of separate systems
- Single render call instead of multiple passes
- Simpler code, clearer intent

---

### Rendering Order Calculation

**Formula**: `sortKey = (elevation * 10000) + yPosition`

**Rationale**:
- Elevation is primary sort (0-15 range)
- Y position is secondary sort (0-65535 range for 4096x4096 map at 16px tiles)
- Multiplier ensures elevation takes precedence (15 * 10000 = 150000, well above max Y)

**Alternative**: Use tuple comparison `(elevation, yPosition)` for cleaner code.

**Example Sort Order**:
1. Elevation 0, Y=100 → Sort key: 100
2. Elevation 0, Y=200 → Sort key: 200
3. Elevation 3, Y=50 → Sort key: 30050
4. Elevation 3, Y=150 → Sort key: 30150
5. Elevation 6, Y=100 → Sort key: 60100

---

### Data Flow

#### Map Loading (MapLoaderSystem)

**Current**: Creates tile chunks with `LayerIndex` and `LayerId`

**Proposed**: 
1. Add `Elevation` property to `MapLayer` class to deserialize existing JSON data
2. Get elevation from `MapLayer.Elevation` (defaults to 3 if not specified in JSON)
3. Add `ElevationComponent` to chunk entities with layer's elevation

**Code Changes**:

**Step 1: Add Elevation to MapLayer class**:
```csharp
// In MapLayer.cs
/// <summary>
/// The elevation level for this layer (0-15).
/// Determines rendering priority and collision behavior.
/// Defaults to 3 if not specified in JSON.
/// </summary>
[JsonPropertyName("elevation")]
public int Elevation { get; set; } = 3; // Default to 3 (standard elevation)
```

**Step 2: Use elevation when creating chunks**:
```csharp
// In MapLoaderSystem.CreateTileChunks()
var layerElevation = layer.Elevation; // Now deserialized from JSON (defaults to 3)

World.Add(chunkEntity, new ElevationComponent { Elevation = (byte)layerElevation });
// Note: TileChunkComponent does NOT get elevation field - use ElevationComponent only
```

#### Entity Creation (NPCs, Players)

**NPCs**:
- Migrate `NpcComponent.Elevation` (currently `int`) → `ElevationComponent` (byte)
- Remove `Elevation` property from `NpcComponent` (breaking change)
- Note: Current `NpcComponent.Elevation` is `int`, but should be `byte` (0-15 range)

**Players**:
- Add `ElevationComponent` with default value 3 (or from constants)
- Update player creation code

#### Border Rendering

**Current**: Borders rendered as direct tile draws (not entities), at implicit elevations (bottom = 3, top = 9)

**Proposed Options**:

**Option A: Keep Borders as Procedural Renders, Integrate into ElevationRendererSystem (Recommended)**
- Borders remain procedural (rendered directly, not as entities)
- Borders rendered within `ElevationRendererSystem` at specific elevation passes:
  - Bottom layer borders: Rendered during elevation 3 pass (with tiles/sprites at elevation 3)
  - Top layer borders: Rendered during elevation 9 pass (with tiles/sprites at elevation 9)
- Maintains elevation-based rendering everywhere (all rendering goes through elevation sorting)
- Simple, matches current architecture while preserving elevation ordering

**Implementation**: 
- `ElevationRendererSystem` calls `MapBorderRendererSystem.RenderBottomLayer()` during elevation 3 pass
- `ElevationRendererSystem` calls `MapBorderRendererSystem.RenderTopLayer()` during elevation 9 pass
- Borders rendered within the same `SpriteBatch` session for proper interleaving

**Option B: Convert Borders to Entities** (Future Enhancement)
- Create border tile entities with `ElevationComponent`
- Borders collected and sorted with other renderables
- More flexible, but more complex

**Recommendation**: Option A - Keep borders procedural but integrate into elevation-based rendering system. This maintains elevation ordering everywhere while keeping the simple procedural rendering approach. Borders don't need per-tile elevation sorting since they're always at fixed elevations (3 and 9).

---

### Shader Stacking Considerations

**Current**: Each system (MapRendererSystem, SpriteRendererSystem) handles its own shader stacking

**Proposed**: `ElevationRendererSystem` handles shader stacking for all renderable types

**Challenges**:
1. Different renderable types may have different shader stacks (tile shaders vs sprite shaders)
2. Shader stacking requires render targets, which complicates interleaved rendering

**Solutions**:

**Option A: Render to Separate Render Targets, Then Composite**
1. Render tiles to render target A (with tile shader stack)
2. Render sprites to render target B (with sprite shader stack)
3. Composite A and B by elevation (expensive, requires per-pixel depth sorting)

**Option B: Render in Elevation Order, Batch by Shader**
1. Sort all renderables by elevation + Y
2. Group consecutive renderables with same shader stack
3. Render each group to render target, apply shader stack
4. Composite render targets in elevation order (still complex)

**Option C: Simplified Shader Stacking (Recommended - Default)**
1. For elevation-based rendering, use single shader stack (combined layer shaders)
2. Per-entity shaders still supported (applied by helper renderers during geometry rendering)
3. Tile/sprite layer shaders merged into combined layer shaders (post-processing)
4. Simpler architecture, matches current post-processing approach
5. **Performance**: Negligible overhead (~0.01ms sorting overhead vs current system)

**Limitation**: Cannot support effects that require separate tile/sprite render targets (reflections, shadows, etc.)

**Performance Note**: Option C should be the default choice. Option D should only be used when effect shaders are detected.

**Option D: Multi-Target with Effect Layers (Use Only When Effects Needed)**
1. Render tiles to render target A (sorted by elevation)
2. Render sprites to render target B (sorted by elevation)
3. Apply effect shaders (shadows, reflections) that sample from B and composite with A
4. Composite final result: render A, apply effects, render B
5. Apply combined layer shader stack as post-processing

**Benefits**:
- Supports sprite reflections (sprites reflect in water tiles)
- Supports sprite shadows (sprites cast shadows on tiles)
- Supports other effects that require tile/sprite interaction
- Maintains elevation-based sorting within each layer

**Implementation**:
```csharp
// 1. Render tiles to render target A (sorted by elevation)
RenderTilesToTarget(tileRenderTarget);

// 2. Render sprites to render target B (sorted by elevation)
RenderSpritesToTarget(spriteRenderTarget);

// 3. Composite with effects:
// - Render tile render target
// - Apply shadow shader (samples sprite render target, projects shadows)
// - Apply reflection shader (samples sprite render target, reflects in water)
// - Render sprite render target
_spriteBatch.Begin();
_spriteBatch.Draw(tileRenderTarget, ...);
ApplyShadowEffect(spriteRenderTarget, ...);  // Composite shadows
ApplyReflectionEffect(spriteRenderTarget, ...);  // Composite reflections
_spriteBatch.Draw(spriteRenderTarget, ...);
_spriteBatch.End();

// 4. Apply combined layer shader stack (post-processing)
ApplyShaderStack(compositeTarget, null, shaderStack, ...);
```

**Trade-offs**:
- More complex than Option C (requires managing multiple render targets)
- Effects must be applied at composite step (before final rendering)
- Elevation-based interleaving lost at effect boundaries (tiles always render before sprites in final composite)
- **Performance**: ~3-5x slower than Option C (3 render targets vs 1, 2x geometry passes, composite pass)

**Recommendation**: 
- **Option C (Simplified)** is the default - use this when effects aren't needed (simpler, better performance)
- **Option D (Multi-Target)** only when effect shaders are detected (reflections, shadows, etc.)
- **Implementation**: Automatically detect if effect shaders are active, use Option D if needed, Option C otherwise

**Performance Impact**:
- Option C: Negligible overhead (~0.01ms sorting overhead vs current system)
- Option D: Significant overhead (~3-5x slower) but necessary for effects support

---

## Migration Strategy

### Phase 1: Component Creation and Data Migration

1. **Create `ElevationComponent`**
   - Add component definition
   - Add to `MonoBall.Core/ECS/Components/`

2. **Migrate NPC Elevation**
   - Update NPC creation to add `ElevationComponent`
   - Remove `Elevation` from `NpcComponent` (breaking change)
   - Update all code that reads `NpcComponent.Elevation`

3. **Add Player Elevation**
   - Add `ElevationComponent` to player entities
   - Default to 3, or read from constants

4. **Add Elevation to Tile Chunks**
   - Add `Elevation` property to `MapLayer` class (JSON already has this data - see `LittlerootTown.json` with elevation: 0, 3, 15)
   - Update `MapLoaderSystem` to add `ElevationComponent` to chunk entities
   - Get elevation from `MapLayer.Elevation` (deserialized from JSON, defaults to 3)
   - **Note**: Do NOT add elevation field to `TileChunkComponent` - use `ElevationComponent` only (single source of truth)
   - **Note**: JSON map definitions already include elevation data, it just needs to be deserialized by the C# class

### Phase 2: ElevationRendererSystem Implementation

1. **Create Helper Renderer Classes**
   - Extract tile rendering logic to `TileChunkRenderer` helper class
   - Extract sprite rendering logic to `SpriteRenderer` helper class
   - Maintains separation of concerns (SRP)

2. **Create `ElevationRendererSystem`**
   - Implement unified collection and sorting
   - Coordinate rendering using helper renderers
   - Implement single SpriteBatch session for interleaved rendering

2. **Test with Simple Case**
   - Test with single elevation level
   - Verify tiles and sprites render correctly

3. **Add Multi-Elevation Support**
   - Test with multiple elevation levels
   - Verify sorting works correctly

### Phase 3: Integration

1. **Update GameSceneSystem**
   - Replace separate system calls with `ElevationRendererSystem.Render()`
   - Test scene rendering

2. **Update Border Rendering**
   - Keep `MapBorderRendererSystem` unchanged (borders remain procedural)
   - Borders rendered at fixed elevations (bottom = 3, top = 9)
   - Integration options:
     - **Option A**: Call border rendering at elevation passes (after elevation 0-2, before 3+ for bottom; after 0-8, before 9+ for top)
     - **Option B**: Keep current approach (borders before/after sprites) - simpler, elevation-based sorting not needed for borders
   - **Recommendation**: Option B - Keep current approach. Borders don't need elevation-based sorting since they're at fixed elevations.

3. **Remove Deprecated Systems**
   - Remove `MapRendererSystem` (logic moved to `TileChunkRenderer` helper)
   - Remove `SpriteRendererSystem` (logic moved to `SpriteRenderer` helper)
   - Keep `MapBorderRendererSystem` (borders remain procedural)
   - Per cursorrules: "NO BACKWARD COMPATIBILITY - Refactor APIs freely, break existing code if needed"

### Phase 4: Per-Tile Elevation (Future Enhancement)

1. **Add Per-Tile Elevation Storage**
   - Store elevation per-tile in `TileDataComponent` (array)
   - Use chunk elevation as default, tile elevation as override

2. **Update Rendering Logic**
   - Check per-tile elevation when rendering
   - Sort tiles within chunk by elevation

---

## Implementation Details

### RenderableItem Structure

```csharp
/// <summary>
/// Base renderable item - uses inheritance for type-specific data to avoid memory waste.
/// Each derived type only contains relevant fields.
/// </summary>
private abstract class RenderableItem
{
    public Entity Entity { get; init; }
    public byte Elevation { get; init; }
    public float YPosition { get; init; }

    public abstract void Render(SpriteBatch spriteBatch, ITileChunkRenderer tileRenderer, ISpriteRenderer spriteRenderer);
}

private sealed class TileChunkRenderableItem : RenderableItem
{
    public required TileChunkComponent Chunk { get; init; }
    public required TileDataComponent Data { get; init; }
    public required PositionComponent Position { get; init; }
    public required RenderableComponent Render { get; init; }

    public override void Render(SpriteBatch spriteBatch, ITileChunkRenderer tileRenderer, ISpriteRenderer spriteRenderer)
    {
        tileRenderer.RenderChunk(Entity, Chunk, Data, Position, Render, spriteBatch);
    }
}

private sealed class SpriteRenderableItem : RenderableItem
{
    public required SpriteComponent Sprite { get; init; }
    public required PositionComponent Position { get; init; }
    public required RenderableComponent Render { get; init; }

    public override void Render(SpriteBatch spriteBatch, ITileChunkRenderer tileRenderer, ISpriteRenderer spriteRenderer)
    {
        spriteRenderer.RenderSprite(Entity, Sprite, Position, Render, spriteBatch);
    }
}
```

**Note**: Borders are not included in RenderableItem since they're procedural and rendered separately.

**Rationale**:
- Single list allows unified sorting by elevation + Y position
- Polymorphic design eliminates switch statements and type discriminators
- Each derived type only contains its relevant component data (no memory waste)
- `Render()` method provides clean dispatch without runtime type checks
- Sealed classes enable devirtualization optimizations

### Sorting Strategy

**Option A: Single List Sort**
- Collect all renderables in single list
- Sort by elevation, then Y position
- Render in order

**Pros**: Simple, clear intent  
**Cons**: Larger list, more comparisons

**Option B: Separate Lists, Merge During Render**
- Collect tiles, sprites, borders in separate lists
- Sort each list by elevation + Y
- Merge during rendering (iterate all lists, render lowest elevation/Y)

**Pros**: Smaller lists, better cache locality  
**Cons**: More complex merge logic

**Recommendation**: Option A - Single list sort is simpler and performance difference is negligible for typical entity counts.

### Y Position Calculation

**Y Position Calculation** (extracted to helper methods):

**Tiles**: Use chunk's bottom Y position (chunk Y + chunk height in tiles, converted to pixels)

**Sprites**: Use sprite's bottom Y position (position.Y + sprite frame height)

**Formula**:
```csharp
/// <summary>
/// Calculates the bottom Y position for elevation sorting.
/// Uses entity's bottom edge to ensure proper Y-sorting.
/// </summary>
private float CalculateTileBottomY(TileChunkComponent chunk, PositionComponent pos)
{
    // Get tile dimensions from camera/tileset
    var tileHeight = GetTileHeight(); // From camera or tileset definition
    return pos.Position.Y + (chunk.ChunkHeight * tileHeight);
}

/// <summary>
/// Calculates the bottom Y position for elevation sorting.
/// Uses sprite's bottom edge (position + frame height).
/// </summary>
private float CalculateSpriteBottomY(SpriteComponent sprite, PositionComponent pos)
{
    // Get sprite definition for frame height
    var spriteDef = _resourceManager.GetSpriteDefinition(sprite.SpriteId);
    if (spriteDef == null)
        throw new InvalidOperationException($"SpriteDefinition not found: {sprite.SpriteId}");

    return pos.Position.Y + spriteDef.FrameHeight;
}
```

**Note**: Borders are not included - they're procedural and rendered at fixed elevations.

---

## Performance Considerations

### Sorting Performance

**Current System**: 
- Tile chunks: ~100-500 chunks, sorted by LayerIndex/LayerId (~0.1ms)
- Sprites: ~10-100 sprites, sorted by RenderOrder (~0.01ms)
- Total: ~0.11ms

**Proposed System**:
- All renderables: ~110-600 items, sorted by elevation + Y position (~0.12ms)

**Impact**: Negligible overhead (~0.01ms) - single sort of 600 items is fast, similar to current two separate sorts.

**Optimization**: Current approach is optimal. Sorting 600 items is not a bottleneck.

### Collection Performance

**Current**: Separate queries for tiles, sprites, borders

**Proposed**: Separate queries, then combine into single list

**Impact**: Minimal - combining lists is O(n), very fast (~0.001ms for 600 items).

**Optimization**: Current approach is optimal - separate queries are efficient (Arch ECS optimized).

### Culling Performance

**Current**: Each system culls independently (before sorting)

**Proposed**: Cull before adding to renderable list (before sorting)

**Impact**: Same - culling happens before sorting in both cases. Only visible entities are sorted.

**Optimization**: Current approach is optimal - culling before sorting avoids wasted work.

### SpriteBatch Batch Breaking

**Current**: Each system batches by shader/material (SpriteRendererSystem pre-sorts sprites by shader)

**Proposed**: Render in elevation order, restart SpriteBatch when per-entity shader changes

**Impact**: 
- May cause more SpriteBatch.Begin/End calls (batch breaks)
- SpriteBatch.Begin/End has overhead (~0.01-0.1ms per call)
- Estimated: 5-20 batch breaks per frame (depending on shader diversity)

**Analysis**: 
- Rendering in elevation order intermixes entities with different per-entity shaders
- Cannot pre-sort by shader (would break elevation ordering)
- Batch breaks are acceptable for initial implementation

**Recommendation**: 
- **Initial Implementation**: Accept batch breaks (render in elevation order, restart SpriteBatch on shader change)
- **Future Optimization**: If profiling shows SpriteBatch overhead is significant (>1ms), implement shader grouping within elevation groups (complex, maintain elevation order within shader groups)

### Render Target Performance (Option D - Multi-Target)

**Option C (Simplified)**:
- Single render target (if post-processing shaders active)
- Single geometry pass
- **Performance**: Baseline (negligible overhead vs current system)

**Option D (Multi-Target)**:
- Three render targets (tiles, sprites, composite)
- Two geometry passes (tiles, sprites) + composite pass
- **Performance**: ~3-5x slower than Option C

**Recommendation**: 
- **Always prefer Option C** when effects aren't needed
- **Only use Option D** when effect shaders are detected
- Automatically detect effect shaders, fallback to Option C if none

### Memory Usage

**Current System**:
- MapRendererSystem: ~500 chunks * 40 bytes = 20KB
- SpriteRendererSystem: ~100 sprites * 40 bytes = 4KB
- **Total**: ~24KB

**Proposed System (Option C)**:
- ElevationRendererSystem: ~600 renderables * 80 bytes = 48KB
- **Overhead**: +24KB (doubled, but still negligible for modern systems)

**Proposed System (Option D)**:
- ElevationRendererSystem: ~600 renderables * 80 bytes = 48KB
- Render targets: 3 * (screen width * screen height * 4 bytes) = ~12MB @ 1920x1080
- **Note**: Render targets are shared/reused via RenderTargetManager (not allocated per frame)

**Impact**: Memory overhead is acceptable. Render target memory is significant but cached.

### Performance Benchmarks (Estimated)

**Current System (Layer-Based)**:
- Sorting: ~0.11ms
- Rendering: Variable (depends on visible items)

**Proposed System - Option C (Simplified)**:
- Sorting: ~0.12ms (negligible overhead)
- Rendering: Similar to current (same geometry, just sorted differently)
- **Total Overhead**: ~0.01ms (negligible)

**Proposed System - Option D (Multi-Target)**:
- Sorting: ~0.12ms (same as Option C)
- Render targets: 3x operations = ~3x cost
- Rendering: 2x geometry passes = ~2x cost
- Composite: Additional pass = +1x cost
- **Total Overhead**: ~5-6x compared to Option C
- **Use Case**: Only when effect shaders are active (reflections, shadows, etc.)

**Conclusion**: Option C has negligible performance impact. Option D has significant cost but is necessary for effects support. Always prefer Option C when effects aren't needed.

---

## Testing Strategy

### Unit Tests

1. **Sorting Tests**
   - Test elevation-based sorting (elevation 0 before 3)
   - Test Y position sorting within same elevation
   - Test edge cases (same elevation, same Y)

2. **Collection Tests**
   - Test collection of tiles, sprites, borders
   - Test culling logic

### Integration Tests

1. **Single Elevation**
   - Render scene with all entities at elevation 3
   - Verify rendering matches current system

2. **Multiple Elevations**
   - Render scene with entities at elevations 0, 3, 6, 9
   - Verify correct render order

3. **Y Position Sorting**
   - Render entities at same elevation, different Y positions
   - Verify correct render order (lower Y renders first)

### Visual Tests

1. **Bridge Over Water**
   - Water at elevation 0, bridge at elevation 6
   - Player on bridge at elevation 6
   - Verify player appears on top of bridge, bridge on top of water

2. **Multi-Level Building**
   - Ground floor at elevation 3
   - Second floor at elevation 6
   - Roof at elevation 9
   - Verify correct layering

---

## Open Questions

1. **MapLayer.Elevation**: Does `MapLayer` already have elevation, or do we need to add it?
   - **Answer**: JSON map definitions already include `"elevation"` fields on layers (confirmed via `LittlerootTown.json` - layers have elevation: 0, 3, 15)
   - **Issue**: The C# `MapLayer` class does NOT have an `Elevation` property, so the JSON data is not being deserialized
   - **Action Required**: Add `Elevation` property to `MapLayer` class with `[JsonPropertyName("elevation")]` attribute (default 3)
   - **Note**: The data exists in JSON but is currently being ignored during deserialization

2. **Per-Tile Elevation**: Should we support per-tile elevation from the start, or add later?
   - **Recommendation**: Start with per-chunk elevation, add per-tile later
   - **Rationale**: Simpler implementation, covers most use cases

3. **Border Elevation**: Should border elevations be configurable, or fixed?
   - **Answer**: Fixed for now (bottom = 3, top = 9)
   - **Rationale**: Borders are procedural (not entities), so elevation is implicit in render order
   - **Current Approach**: Borders rendered separately, not part of elevation-based system
   - **Future**: If borders need per-border elevation, convert to entities with `ElevationComponent` (Option B from Border Rendering section)

4. **Shader Stacking**: How to handle different shader stacks for tiles vs sprites?
   - **Answer**: Use Option C (Simplified) as default, Option D (Multi-Target) only when effects are detected
   - **Rationale**: Option C is 3-5x faster and sufficient for most cases. Option D supports sprite reflections/shadows but has significant performance cost.
   - **Implementation**: Automatically detect effect shaders, use Option D if needed, Option C otherwise
   - **Performance**: Option C has negligible overhead (~0.01ms). Option D is ~3-5x slower but necessary for effects.

5. **Backward Compatibility**: Should we maintain `MapRendererSystem`/`SpriteRendererSystem` for non-elevation contexts?
   - **Answer**: Remove after migration (per cursorrules: "NO BACKWARD COMPATIBILITY")
   - **Rationale**: Rendering logic moved to helper classes (`TileChunkRenderer`, `SpriteRenderer`)
   - **No non-elevation contexts**: All game scene rendering uses elevation-based system

---

## References

- **Pokemon Emerald Elevation System**: `oldmonoball/MonoBallFramework.Game/Ecs/Components/Rendering/Elevation.cs`
- **Old Elevation Render System**: `oldmonoball/MonoBallFramework.Game/Engine/Rendering/Systems/ElevationRenderSystem.cs`
- **Current Map Renderer**: `MonoBall.Core/ECS/Systems/MapRendererSystem.cs`
- **Current Sprite Renderer**: `MonoBall.Core/ECS/Systems/SpriteRendererSystem.cs`
- **Current Game Scene**: `MonoBall.Core/Scenes/Systems/GameSceneSystem.cs`
- **Collision Elevation Analysis**: `MonoBall.Core/docs/design/collision-elevation-analysis.md`

---

## Appendix: Code Examples

### ElevationComponent Usage

```csharp
// Creating entity with elevation
World.Add(entity, new ElevationComponent { Elevation = 3 });

// Reading elevation
ref var elevation = ref World.Get<ElevationComponent>(entity);
byte elev = elevation.Elevation;
```

### ElevationRendererSystem.Render() (Simplified)

See full implementation in "System Changes" section above. Key points:

1. **Collection**: Collects tile chunks and sprites (borders excluded - procedural)
2. **Sorting**: Sorts by elevation, then Y position
3. **Rendering**: Single SpriteBatch session, delegates to helper renderers
4. **Shaders**: Per-entity shaders applied by helpers, layer shaders as post-processing

### Helper Renderer Pattern

Helper renderers (`TileChunkRenderer`, `SpriteRenderer`) are internal classes that encapsulate rendering logic:

```csharp
internal class TileChunkRenderer
{
    // Extracted from MapRendererSystem
    public void RenderChunk(
        Entity entity,
        TileChunkComponent chunk,
        TileDataComponent data,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    )
    {
        // Tile rendering logic (from MapRendererSystem.RenderChunk)
        // Handles tileset resolution, animated tiles, flip flags, etc.
    }
}

internal class SpriteRenderer
{
    // Extracted from SpriteRendererSystem
    public void RenderSprite(
        Entity entity,
        SpriteComponent sprite,
        PositionComponent pos,
        RenderableComponent render,
        SpriteBatch spriteBatch
    )
    {
        // Sprite rendering logic (from SpriteRendererSystem.RenderSprite)
        // Handles sprite textures, frames, per-entity shaders, etc.
    }
}
```

**Benefits**:
- Separation of concerns (SRP) - each helper has single responsibility
- Reusable rendering logic
- Easier to test and maintain
- System coordinates, helpers render

---

## Conclusion

This design provides a clear path to elevation-based rendering with optimal performance. The migration should be done as a single cohesive change, starting with component creation and data migration, then system implementation, and finally integration.

Key benefits:
- Unified rendering system (simpler code)
- True elevation-based sorting (bridges, multi-level maps)
- Consistent data model (`ElevationComponent` for all entities)
- Performance maintained (minimal overhead)

Next steps:
1. Review and approve design
2. Answer open questions
3. Begin Phase 1 implementation (component creation and data migration)
