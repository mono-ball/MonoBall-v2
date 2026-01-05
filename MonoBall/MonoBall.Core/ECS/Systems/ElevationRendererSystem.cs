using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Constants;
using MonoBall.Core.ECS.Rendering;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Unified rendering system that sorts all renderables (tiles + sprites) by elevation,
///     then Y position for correct depth ordering. Replaces MapRendererSystem and SpriteRendererSystem.
/// </summary>
public sealed class ElevationRendererSystem : BaseSystem<World, float>
{
    private readonly ICameraService _cameraService;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;
    private readonly MapBorderRendererSystem? _mapBorderRendererSystem;
    private readonly RenderTargetManager? _renderTargetManager;
    private readonly IResourceManager _resourceManager;
    private readonly ShaderManagerSystem? _shaderManagerSystem;
    private readonly ShaderRendererSystem? _shaderRendererSystem;
    private readonly ISpriteRenderer _spriteRenderer;
    private readonly ITileChunkRenderer _tileChunkRenderer;

    // Static query descriptions (avoids per-instance allocation)
    private static readonly QueryDescription TileChunkQuery = new QueryDescription().WithAll<
        TileChunkComponent,
        TileDataComponent,
        PositionComponent,
        RenderableComponent,
        ElevationComponent
    >();

    private static readonly QueryDescription NpcSpriteQuery = new QueryDescription().WithAll<
        NpcComponent,
        SpriteComponent,
        PositionComponent,
        RenderableComponent,
        ElevationComponent,
        ActiveMapEntity
    >();

    private static readonly QueryDescription PlayerSpriteQuery = new QueryDescription().WithAll<
        PlayerComponent,
        SpriteSheetComponent,
        SpriteComponent,
        PositionComponent,
        RenderableComponent,
        ElevationComponent
    >();

    // Reusable collection to avoid allocations in hot paths
    private readonly List<RenderableItem> _renderableItems = new();

    // Cached static comparer for elevation + render order + Y sorting (avoids lambda allocation)
    // Sort order: elevation first, then render order (tiles before sprites), then Y position
    private static readonly Comparison<RenderableItem> ElevationComparer = (a, b) =>
    {
        var elevationComparison = a.Elevation.CompareTo(b.Elevation);
        if (elevationComparison != 0)
            return elevationComparison;

        var renderOrderComparison = a.RenderOrder.CompareTo(b.RenderOrder);
        if (renderOrderComparison != 0)
            return renderOrderComparison;

        return a.YPosition.CompareTo(b.YPosition);
    };

    private readonly PerformanceStatsSystem? _performanceStatsSystem;
    private readonly SpriteBatch _spriteBatch;
    private Viewport _savedViewport;

    /// <summary>
    ///     Render target index for elevation layer shader stacking.
    /// </summary>
    private const int ElevationLayerRenderTargetIndex = 102;

    /// <summary>
    ///     Number of tiles to expand view bounds for culling (prevents edge artifacts).
    /// </summary>
    private const int ViewBoundsExpandTiles = 1;

    /// <summary>
    ///     Initializes a new instance of the ElevationRendererSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device for rendering.</param>
    /// <param name="resourceManager">The resource manager for loading textures and definitions.</param>
    /// <param name="cameraService">The camera service for querying active camera.</param>
    /// <param name="tileChunkRenderer">The tile chunk renderer.</param>
    /// <param name="spriteRenderer">The sprite renderer.</param>
    /// <param name="spriteBatch">The SpriteBatch for rendering.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <param name="mapBorderRendererSystem">The map border renderer system (optional).</param>
    /// <param name="shaderManagerSystem">The shader manager system (optional).</param>
    /// <param name="shaderRendererSystem">The shader renderer system for shader stacking (optional).</param>
    /// <param name="renderTargetManager">The render target manager for shader stacking (optional).</param>
    /// <param name="performanceStatsSystem">The performance stats system for tracking draw calls (optional).</param>
    public ElevationRendererSystem(
        World world,
        GraphicsDevice graphicsDevice,
        IResourceManager resourceManager,
        ICameraService cameraService,
        ITileChunkRenderer tileChunkRenderer,
        ISpriteRenderer spriteRenderer,
        SpriteBatch spriteBatch,
        ILogger logger,
        MapBorderRendererSystem? mapBorderRendererSystem = null,
        ShaderManagerSystem? shaderManagerSystem = null,
        ShaderRendererSystem? shaderRendererSystem = null,
        RenderTargetManager? renderTargetManager = null,
        PerformanceStatsSystem? performanceStatsSystem = null
    )
        : base(world)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        _tileChunkRenderer =
            tileChunkRenderer ?? throw new ArgumentNullException(nameof(tileChunkRenderer));
        _spriteRenderer = spriteRenderer ?? throw new ArgumentNullException(nameof(spriteRenderer));
        _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mapBorderRendererSystem = mapBorderRendererSystem;
        _shaderManagerSystem = shaderManagerSystem;
        _shaderRendererSystem = shaderRendererSystem;
        _renderTargetManager = renderTargetManager;
        _performanceStatsSystem = performanceStatsSystem;
    }

    /// <summary>
    ///     Renders all visible entities sorted by elevation, then Y position.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, uses global shaders only.</param>
    public void Render(GameTime gameTime, Entity? sceneEntity = null)
    {
        // Get active camera
        var activeCamera = _cameraService.GetActiveCamera();
        if (!activeCamera.HasValue)
            return;

        var camera = activeCamera.Value;

        // Get visible bounds for culling
        var visiblePixelBounds = CalculateVisiblePixelBounds(camera);

        // Clear and collect all renderables
        _renderableItems.Clear();
        CollectTileChunks(visiblePixelBounds);
        CollectSprites(visiblePixelBounds);

        if (_renderableItems.Count == 0)
            return;

        // Sort by elevation, then Y position
        _renderableItems.Sort(ElevationComparer);

        // Save original viewport
        _savedViewport = _graphicsDevice.Viewport;

        try
        {
            // Set viewport to camera.VirtualViewport if not empty
            SetupRenderViewport(camera);

            // Get camera transform matrix
            var transform = camera.GetTransformMatrix();

            // Check for shader stacking
            var shaderStack = _shaderManagerSystem?.GetTileLayerShaderStack(sceneEntity);
            var needsShaderStacking = ShaderStackingHelper.NeedsShaderStacking(shaderStack);

            if (needsShaderStacking)
            {
                needsShaderStacking = ShaderStackingHelper.ValidateShaderStackingDependencies(
                    _shaderRendererSystem,
                    _renderTargetManager,
                    _logger,
                    "ElevationRendererSystem"
                );
            }

            if (
                needsShaderStacking
                && _renderTargetManager != null
                && _shaderRendererSystem != null
            )
            {
                RenderWithShaderStacking(gameTime, transform, shaderStack!);
            }
            else
            {
                RenderDirect(gameTime, transform, shaderStack);
            }
        }
        finally
        {
            // Always restore original viewport
            _graphicsDevice.Viewport = _savedViewport;
        }
    }

    /// <summary>
    ///     Renders with shader stacking to a render target.
    /// </summary>
    private void RenderWithShaderStacking(
        GameTime gameTime,
        Matrix transform,
        IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)> shaderStack
    )
    {
        var renderTarget = ShaderStackingHelper.GetOrCreateRenderTargetForStacking(
            _graphicsDevice,
            _renderTargetManager!,
            ElevationLayerRenderTargetIndex,
            _logger,
            "ElevationRendererSystem"
        );

        if (renderTarget == null)
        {
            // Fallback to direct rendering
            RenderDirect(gameTime, transform, shaderStack);
            return;
        }

        // Save previous render target
        var renderTargets = _graphicsDevice.GetRenderTargets();
        var previousTarget =
            renderTargets.Length > 0 ? renderTargets[0].RenderTarget as RenderTarget2D : null;
        var needToSetTarget = ShaderStackingHelper.SetupRenderTarget(_graphicsDevice, renderTarget);

        try
        {
            // Render to render target without layer shader (ApplyShaderStack handles all)
            RenderAllItems(gameTime, transform, null);

            // Apply shader stack
            _shaderRendererSystem!.ApplyShaderStack(
                renderTarget,
                null, // Render to back buffer
                shaderStack,
                _spriteBatch,
                _graphicsDevice,
                _renderTargetManager!
            );

            _performanceStatsSystem?.IncrementDrawCalls();
        }
        finally
        {
            if (needToSetTarget)
                _graphicsDevice.SetRenderTarget(previousTarget);
        }
    }

    /// <summary>
    ///     Renders directly without shader stacking.
    /// </summary>
    private void RenderDirect(
        GameTime gameTime,
        Matrix transform,
        IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)>? shaderStack
    )
    {
        var layerShader =
            shaderStack != null && shaderStack.Count > 0 ? shaderStack[0].effect : null;
        RenderAllItems(gameTime, transform, layerShader);
        _performanceStatsSystem?.IncrementDrawCalls();
    }

    /// <summary>
    ///     Renders all items with elevation-based border integration.
    /// </summary>
    private void RenderAllItems(GameTime gameTime, Matrix transform, Effect? layerShader)
    {
        // Ensure CurrentTechnique is set
        if (layerShader != null)
            ShaderParameterApplier.EnsureCurrentTechnique(layerShader, _logger);

        // Track if we've rendered borders at each elevation
        var renderedBottomBorder = false;
        var renderedTopBorder = false;

        // Begin SpriteBatch
        _spriteBatch.Begin(
            SpriteSortMode.Immediate,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            null,
            null,
            layerShader,
            transform
        );

        foreach (var item in _renderableItems)
        {
            // Render bottom border at elevation threshold
            if (!renderedBottomBorder && item.Elevation >= ElevationConstants.BorderBottomElevation)
            {
                _mapBorderRendererSystem?.RenderBottomLayerInBatch(gameTime, _spriteBatch);
                renderedBottomBorder = true;
            }

            // Render top border at elevation threshold
            if (!renderedTopBorder && item.Elevation >= ElevationConstants.BorderTopElevation)
            {
                _mapBorderRendererSystem?.RenderTopLayerInBatch(gameTime, _spriteBatch);
                renderedTopBorder = true;
            }

            // Render the item
            item.Render(World, _spriteBatch, _tileChunkRenderer, _spriteRenderer);
        }

        // Render any remaining borders if not yet rendered
        if (!renderedBottomBorder)
            _mapBorderRendererSystem?.RenderBottomLayerInBatch(gameTime, _spriteBatch);

        if (!renderedTopBorder)
            _mapBorderRendererSystem?.RenderTopLayerInBatch(gameTime, _spriteBatch);

        _spriteBatch.End();
    }

    /// <summary>
    ///     Collects visible tile chunks into the renderables list.
    /// </summary>
    /// <param name="visiblePixelBounds">The visible pixel bounds for culling.</param>
    private void CollectTileChunks(Rectangle visiblePixelBounds)
    {
        World.Query(
            in TileChunkQuery,
            (
                Entity entity,
                ref TileChunkComponent chunk,
                ref TileDataComponent data,
                ref PositionComponent pos,
                ref RenderableComponent render,
                ref ElevationComponent elevation
            ) =>
            {
                if (!render.IsVisible)
                    return;

                // Get tile dimensions from tileset for accurate culling
                var tilesetDef = _resourceManager.GetTilesetDefinition(data.TilesetId);

                // Calculate chunk bounds
                var chunkBounds = new Rectangle(
                    (int)pos.Position.X,
                    (int)pos.Position.Y,
                    chunk.ChunkWidth * tilesetDef.TileWidth,
                    chunk.ChunkHeight * tilesetDef.TileHeight
                );

                if (!chunkBounds.Intersects(visiblePixelBounds))
                    return;

                // Use bottom edge of chunk for Y-sorting (ensures correct depth ordering)
                var chunkBottomY = pos.Position.Y + chunk.ChunkHeight * tilesetDef.TileHeight;

                _renderableItems.Add(
                    new TileChunkRenderableItem
                    {
                        Entity = entity,
                        Elevation = elevation.Value,
                        YPosition = chunkBottomY,
                        Chunk = chunk,
                        Data = data,
                        Position = pos,
                        Renderable = render,
                    }
                );
            }
        );
    }

    /// <summary>
    ///     Collects visible sprites (NPCs and Players) into the renderables list.
    /// </summary>
    private void CollectSprites(Rectangle visiblePixelBounds)
    {
        // Query NPCs
        World.Query(
            in NpcSpriteQuery,
            (
                Entity entity,
                ref NpcComponent npc,
                ref SpriteComponent sprite,
                ref PositionComponent pos,
                ref RenderableComponent render,
                ref ElevationComponent elevation
            ) =>
            {
                TryAddSpriteRenderable(
                    entity,
                    ref sprite,
                    ref pos,
                    ref render,
                    ref elevation,
                    visiblePixelBounds
                );
            }
        );

        // Query Players
        World.Query(
            in PlayerSpriteQuery,
            (
                Entity entity,
                ref PlayerComponent player,
                ref SpriteSheetComponent spriteSheet,
                ref SpriteComponent sprite,
                ref PositionComponent pos,
                ref RenderableComponent render,
                ref ElevationComponent elevation
            ) =>
            {
                TryAddSpriteRenderable(
                    entity,
                    ref sprite,
                    ref pos,
                    ref render,
                    ref elevation,
                    visiblePixelBounds
                );
            }
        );
    }

    /// <summary>
    ///     Attempts to add a sprite to the renderables list if visible and within bounds.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="sprite">The sprite component.</param>
    /// <param name="pos">The position component.</param>
    /// <param name="render">The renderable component.</param>
    /// <param name="elevation">The elevation component.</param>
    /// <param name="visiblePixelBounds">The visible pixel bounds for culling.</param>
    private void TryAddSpriteRenderable(
        Entity entity,
        ref SpriteComponent sprite,
        ref PositionComponent pos,
        ref RenderableComponent render,
        ref ElevationComponent elevation,
        Rectangle visiblePixelBounds
    )
    {
        if (!render.IsVisible)
            return;

        // Get sprite definition for bounds
        var spriteDef = _resourceManager.GetSpriteDefinition(sprite.SpriteId);
        if (spriteDef == null)
            return;

        // Get active camera for tile dimensions
        var activeCamera = _cameraService.GetActiveCamera();
        var tileHeight = activeCamera?.TileHeight ?? 16;

        // Calculate visual bounds based on whether this is a multi-tile sprite
        // Multi-tile sprites (height > tileHeight) use feet-based positioning:
        // - They render UPWARD from (PixelX, PixelY + TileHeight)
        // - Visual top is at PixelY + TileHeight - FrameHeight
        // - Visual bottom is at PixelY + TileHeight
        Rectangle spriteBounds;
        float sortingY;

        if (spriteDef.FrameHeight > tileHeight)
        {
            // Multi-tile sprite: calculate visual bounds with feet-based rendering
            // Sprite renders from (PixelY + TileHeight - FrameHeight) to (PixelY + TileHeight)
            var visualTop = pos.PixelY + tileHeight - spriteDef.FrameHeight;
            var visualBottom = pos.PixelY + tileHeight;

            spriteBounds = new Rectangle(
                (int)pos.PixelX,
                (int)visualTop,
                spriteDef.FrameWidth,
                spriteDef.FrameHeight
            );

            // Use feet position (bottom of feet tile) for Y-sorting
            // This ensures correct depth ordering based on where entity stands
            sortingY = visualBottom;
        }
        else
        {
            // Single-tile sprite: standard top-left bounds
            spriteBounds = new Rectangle(
                (int)pos.Position.X,
                (int)pos.Position.Y,
                spriteDef.FrameWidth,
                spriteDef.FrameHeight
            );

            // Use bottom edge for Y-sorting
            sortingY = pos.Position.Y + spriteDef.FrameHeight;
        }

        if (!spriteBounds.Intersects(visiblePixelBounds))
            return;

        _renderableItems.Add(
            new SpriteRenderableItem
            {
                Entity = entity,
                Elevation = elevation.Value,
                YPosition = sortingY,
                Sprite = sprite,
                Position = pos,
                Renderable = render,
            }
        );
    }

    /// <summary>
    ///     Calculates the visible pixel bounds for culling.
    /// </summary>
    private Rectangle CalculateVisiblePixelBounds(CameraComponent camera)
    {
        var tileViewBounds = camera.GetTileViewBounds();

        // Expand bounds to include edge entities
        var expandedTileBounds = new Rectangle(
            tileViewBounds.X - ViewBoundsExpandTiles,
            tileViewBounds.Y - ViewBoundsExpandTiles,
            tileViewBounds.Width + ViewBoundsExpandTiles * 2,
            tileViewBounds.Height + ViewBoundsExpandTiles * 2
        );

        // Convert to pixel bounds
        return new Rectangle(
            expandedTileBounds.X * camera.TileWidth,
            expandedTileBounds.Y * camera.TileHeight,
            expandedTileBounds.Width * camera.TileWidth,
            expandedTileBounds.Height * camera.TileHeight
        );
    }

    /// <summary>
    ///     Sets up the render viewport based on camera settings.
    /// </summary>
    private void SetupRenderViewport(CameraComponent camera)
    {
        if (camera.VirtualViewport != Rectangle.Empty)
        {
            _graphicsDevice.Viewport = new Viewport(
                camera.VirtualViewport.X,
                camera.VirtualViewport.Y,
                camera.VirtualViewport.Width,
                camera.VirtualViewport.Height
            );
        }
    }

    /// <summary>
    ///     Update method required by BaseSystem, but rendering is done via Render().
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Rendering is done via Render() method called from Game.Draw()
        // This Update() method is a no-op for renderer system
    }
}
