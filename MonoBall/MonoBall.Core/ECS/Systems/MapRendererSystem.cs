using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Maps;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for rendering tile chunks using SpriteBatch.
    /// </summary>
    public class MapRendererSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly IResourceManager _resourceManager;
        private readonly ICameraService _cameraService;
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private readonly ShaderRendererSystem? _shaderRendererSystem;
        private readonly RenderTargetManager? _renderTargetManager;
        private SpriteBatch? _spriteBatch;
        private Viewport _savedViewport;
        private readonly QueryDescription _chunkQueryDescription;
        private PerformanceStatsSystem? _performanceStatsSystem;

        // Reusable collection to avoid allocations in hot paths
        private readonly List<(
            Entity entity,
            TileChunkComponent chunk,
            TileDataComponent data,
            PositionComponent pos,
            RenderableComponent render
        )> _chunkList =
            new List<(
                Entity entity,
                TileChunkComponent chunk,
                TileDataComponent data,
                PositionComponent pos,
                RenderableComponent render
            )>();

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the MapRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device for rendering.</param>
        /// <param name="resourceManager">The resource manager for loading tileset textures and definitions.</param>
        /// <param name="cameraService">The camera service for querying active camera.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="shaderManagerSystem">The shader manager system for tile layer shaders (optional).</param>
        /// <param name="shaderRendererSystem">The shader renderer system for shader stacking (optional).</param>
        /// <param name="renderTargetManager">The render target manager for shader stacking (optional).</param>
        public MapRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            IResourceManager resourceManager,
            ICameraService cameraService,
            ILogger logger,
            ShaderManagerSystem? shaderManagerSystem = null,
            ShaderRendererSystem? shaderRendererSystem = null,
            RenderTargetManager? renderTargetManager = null
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new System.ArgumentNullException(nameof(graphicsDevice));
            _resourceManager =
                resourceManager ?? throw new System.ArgumentNullException(nameof(resourceManager));
            _cameraService =
                cameraService ?? throw new System.ArgumentNullException(nameof(cameraService));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            _shaderManagerSystem = shaderManagerSystem;
            _shaderRendererSystem = shaderRendererSystem;
            _renderTargetManager = renderTargetManager;
            _chunkQueryDescription = new QueryDescription().WithAll<
                TileChunkComponent,
                TileDataComponent,
                PositionComponent,
                RenderableComponent
            >();
        }

        /// <summary>
        /// Sets the SpriteBatch instance to use for rendering.
        /// </summary>
        /// <param name="spriteBatch">The SpriteBatch instance.</param>
        public void SetSpriteBatch(SpriteBatch spriteBatch)
        {
            _spriteBatch = spriteBatch;
        }

        /// <summary>
        /// Sets the PerformanceStatsSystem instance for tracking draw calls.
        /// </summary>
        /// <param name="performanceStatsSystem">The PerformanceStatsSystem instance.</param>
        public void SetPerformanceStatsSystem(PerformanceStatsSystem performanceStatsSystem)
        {
            _performanceStatsSystem =
                performanceStatsSystem
                ?? throw new ArgumentNullException(nameof(performanceStatsSystem));
        }

        /// <summary>
        /// Renders all visible tile chunks.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, uses global shaders only.</param>
        public void Render(GameTime gameTime, Entity? sceneEntity = null)
        {
            if (_spriteBatch == null)
            {
                _logger.Warning("MapRendererSystem.Render called but SpriteBatch is null");
                return;
            }

            // Get active camera
            CameraComponent? activeCamera = _cameraService.GetActiveCamera();
            if (!activeCamera.HasValue)
            {
                // No active camera - this can happen during scene transitions, don't log every frame
                return;
            }

            var camera = activeCamera.Value;

            // Get visible tile bounds from camera (in tile coordinates)
            Rectangle tileViewBounds = camera.GetTileViewBounds();

            // Expand bounds slightly to include edge chunks that might be partially visible
            // This prevents empty rows/columns when moving near map edges
            int expandTiles = 1; // Expand by 1 tile in each direction
            Rectangle expandedTileBounds = new Rectangle(
                tileViewBounds.X - expandTiles,
                tileViewBounds.Y - expandTiles,
                tileViewBounds.Width + (expandTiles * 2),
                tileViewBounds.Height + (expandTiles * 2)
            );

            // Convert to pixel bounds for culling chunks (chunks are positioned in pixels)
            Rectangle visiblePixelBounds = new Rectangle(
                expandedTileBounds.X * camera.TileWidth,
                expandedTileBounds.Y * camera.TileHeight,
                expandedTileBounds.Width * camera.TileWidth,
                expandedTileBounds.Height * camera.TileHeight
            );

            // Clear reusable collection
            _chunkList.Clear();

            // Single-pass query: get all required components at once for efficiency
            // Entity reference is included in the tuple for optional AnimatedTileDataComponent access in RenderChunk
            World.Query(
                in _chunkQueryDescription,
                (
                    Entity entity,
                    ref TileChunkComponent chunkComp,
                    ref TileDataComponent dataComp,
                    ref PositionComponent posComp,
                    ref RenderableComponent renderComp
                ) =>
                {
                    if (!renderComp.IsVisible)
                    {
                        return;
                    }

                    // Get tile dimensions from tileset for accurate culling
                    int tileWidth = 16; // Default fallback
                    int tileHeight = 16;
                    try
                    {
                        var tilesetDef = _resourceManager.GetTilesetDefinition(dataComp.TilesetId);
                        tileWidth = tilesetDef.TileWidth;
                        tileHeight = tilesetDef.TileHeight;
                    }
                    catch (Exception ex)
                    {
                        // Log but continue with defaults (culling will be less accurate)
                        _logger.Warning(
                            ex,
                            "MapRendererSystem: Failed to get tileset definition for culling, using defaults. TilesetId: {TilesetId}",
                            dataComp.TilesetId
                        );
                    }

                    // Cull chunks outside visible bounds (chunks are positioned in pixels)
                    Rectangle chunkBounds = new Rectangle(
                        (int)posComp.Position.X,
                        (int)posComp.Position.Y,
                        chunkComp.ChunkWidth * tileWidth,
                        chunkComp.ChunkHeight * tileHeight
                    );

                    if (chunkBounds.Intersects(visiblePixelBounds))
                    {
                        _chunkList.Add((entity, chunkComp, dataComp, posComp, renderComp));
                    }
                }
            );

            // Found visible chunks - no logging needed (this happens every frame)

            if (_chunkList.Count == 0)
            {
                return;
            }

            // Sort by render order (layer index) - in-place sort to avoid allocation
            _chunkList.Sort(
                (a, b) =>
                {
                    int layerIndexComparison = a.chunk.LayerIndex.CompareTo(b.chunk.LayerIndex);
                    if (layerIndexComparison != 0)
                    {
                        return layerIndexComparison;
                    }
                    return string.Compare(
                        a.chunk.LayerId,
                        b.chunk.LayerId,
                        StringComparison.Ordinal
                    );
                }
            );

            // Save original viewport
            _savedViewport = _graphicsDevice.Viewport;

            try
            {
                // Set GraphicsDevice viewport to VirtualViewport for proper letterboxing/pillarboxing
                // This ensures even bars on all sides by rendering to the centered viewport
                Viewport renderViewport = _savedViewport;
                if (camera.VirtualViewport != Rectangle.Empty)
                {
                    renderViewport = new Viewport(
                        camera.VirtualViewport.X,
                        camera.VirtualViewport.Y,
                        camera.VirtualViewport.Width,
                        camera.VirtualViewport.Height
                    );
                    _graphicsDevice.Viewport = renderViewport;
                }

                // Get camera transform matrix (handles tile-to-pixel conversion, zoom, and centering)
                // The matrix centers the world view within the viewport
                // Note: The transform uses camera.Viewport (logical size), which matches renderViewport size
                Matrix transform = camera.GetTransformMatrix();

                // Get tile layer shader stack (filtered by scene if provided)
                var shaderStack = _shaderManagerSystem?.GetTileLayerShaderStack(sceneEntity);
                bool needsShaderStacking =
                    shaderStack != null
                    && shaderStack.Count > 0
                    && (
                        shaderStack.Count > 1 || shaderStack[0].blendMode != ShaderBlendMode.Replace
                    );

                if (
                    needsShaderStacking
                    && (_shaderRendererSystem == null || _renderTargetManager == null)
                )
                {
                    // Shader stacking requested but dependencies missing - fall back to single shader
                    _logger.Warning(
                        "MapRendererSystem: Shader stacking requested but ShaderRendererSystem or RenderTargetManager not available. Falling back to single shader."
                    );
                    needsShaderStacking = false;
                }

                if (needsShaderStacking)
                {
                    // Check if render target is already set (e.g., by SceneRendererSystem for post-processing)
                    var currentRenderTargets = _graphicsDevice.GetRenderTargets();
                    RenderTarget2D? renderTarget =
                        currentRenderTargets.Length > 0
                            ? currentRenderTargets[0].RenderTarget as RenderTarget2D
                            : null;

                    // If no render target is set, create one for shader stacking
                    if (renderTarget == null)
                    {
                        renderTarget = _renderTargetManager!.GetOrCreateRenderTarget(100); // Use index 100 for tile layer
                        if (renderTarget == null)
                        {
                            _logger.Warning(
                                "MapRendererSystem: Failed to create render target for shader stacking. Falling back to direct rendering."
                            );
                            needsShaderStacking = false;
                        }
                    }

                    if (needsShaderStacking && renderTarget != null)
                    {
                        // Render geometry to render target (without shaders - ApplyShaderStack will handle all shaders)
                        var renderTargets = _graphicsDevice.GetRenderTargets();
                        RenderTarget2D? previousTarget =
                            renderTargets.Length > 0
                                ? renderTargets[0].RenderTarget as RenderTarget2D
                                : null;

                        // Only set render target if it's different from current (avoid unnecessary state changes)
                        bool needToSetTarget = previousTarget != renderTarget;
                        if (needToSetTarget)
                        {
                            _graphicsDevice.SetRenderTarget(renderTarget);
                            _graphicsDevice.Clear(Color.Transparent);
                        }

                        try
                        {
                            // Render chunks without shader - ApplyShaderStack will apply all shaders including first
                            _spriteBatch.Begin(
                                SpriteSortMode.Immediate,
                                BlendState.AlphaBlend,
                                SamplerState.PointClamp,
                                null,
                                null,
                                null, // No shader - let ApplyShaderStack handle all shaders
                                transform
                            );

                            int renderedChunks = 0;
                            int renderedTiles = 0;
                            foreach (var (entity, chunk, data, pos, render) in _chunkList)
                            {
                                var tilesRendered = RenderChunk(entity, chunk, data, pos, render);
                                if (tilesRendered > 0)
                                {
                                    renderedChunks++;
                                    renderedTiles += tilesRendered;
                                }
                            }

                            _spriteBatch.End();

                            // Apply shader stack (includes all shaders, no double application)
                            _shaderRendererSystem!.ApplyShaderStack(
                                renderTarget,
                                null, // Render to back buffer
                                shaderStack!,
                                _spriteBatch,
                                _graphicsDevice,
                                _renderTargetManager!
                            );

                            // Increment draw call counter
                            _performanceStatsSystem?.IncrementDrawCalls();
                        }
                        finally
                        {
                            // Only restore render target if we changed it
                            if (needToSetTarget)
                            {
                                _graphicsDevice.SetRenderTarget(previousTarget);
                            }
                        }
                    }
                }

                if (!needsShaderStacking)
                {
                    // Single shader or no shaders - render normally
                    Effect? tileShader =
                        shaderStack != null && shaderStack.Count > 0 ? shaderStack[0].effect : null;

                    // Ensure CurrentTechnique is set before using with SpriteBatch
                    // MonoGame's SpriteBatch.Begin() will apply the effect automatically
                    if (tileShader != null)
                    {
                        ShaderParameterApplier.EnsureCurrentTechnique(tileShader, _logger);
                    }

                    // Render chunks
                    // Use Immediate mode for custom effects to ensure proper parameter application
                    _spriteBatch.Begin(
                        SpriteSortMode.Immediate,
                        BlendState.AlphaBlend,
                        SamplerState.PointClamp,
                        null,
                        null,
                        tileShader,
                        transform
                    );

                    int renderedChunks = 0;
                    int renderedTiles = 0;
                    foreach (var (entity, chunk, data, pos, render) in _chunkList)
                    {
                        var tilesRendered = RenderChunk(entity, chunk, data, pos, render);
                        if (tilesRendered > 0)
                        {
                            renderedChunks++;
                            renderedTiles += tilesRendered;
                        }
                    }

                    _spriteBatch.End();

                    // Increment draw call counter
                    _performanceStatsSystem?.IncrementDrawCalls();
                }

                // Rendered chunks - no logging needed (this happens every frame)
            }
            finally
            {
                // Always restore original viewport, even if an exception occurred
                _graphicsDevice.Viewport = _savedViewport;
            }
        }

        private int RenderChunk(
            Entity chunkEntity,
            TileChunkComponent chunk,
            TileDataComponent data,
            PositionComponent pos,
            RenderableComponent render
        )
        {
            if (!render.IsVisible)
            {
                // Chunk not visible - no logging needed (this is normal culling behavior)
                return 0;
            }

            if (data.TileIndices == null)
            {
                _logger.Warning(
                    "MapRendererSystem.RenderChunk: Chunk at ({X}, {Y}) has null TileIndices",
                    pos.Position.X,
                    pos.Position.Y
                );
                return 0;
            }

            // Get tileset texture
            Texture2D tilesetTexture;
            try
            {
                tilesetTexture = _resourceManager.LoadTexture(data.TilesetId);
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "MapRendererSystem.RenderChunk: Failed to get tileset texture for {TilesetId} at chunk ({X}, {Y})",
                    data.TilesetId,
                    pos.Position.X,
                    pos.Position.Y
                );
                return 0;
            }

            // Get tileset definition for tile dimensions
            TilesetDefinition tilesetDefinition;
            try
            {
                tilesetDefinition = _resourceManager.GetTilesetDefinition(data.TilesetId);
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "MapRendererSystem.RenderChunk: Failed to get tileset definition for {TilesetId} at chunk ({X}, {Y})",
                    data.TilesetId,
                    pos.Position.X,
                    pos.Position.Y
                );
                return 0;
            }

            // Calculate color with opacity
            var color = Color.White * render.Opacity;

            int tilesRendered = 0;
            int emptyTiles = 0;
            int invalidGids = 0;

            // Fast path: if no animated tiles, render all tiles normally
            if (!data.HasAnimatedTiles)
            {
                // Render each tile in the chunk (fast path - no animation checks)
                for (int y = 0; y < chunk.ChunkHeight; y++)
                {
                    for (int x = 0; x < chunk.ChunkWidth; x++)
                    {
                        int tileIndex = y * chunk.ChunkWidth + x;
                        if (tileIndex >= data.TileIndices.Length)
                        {
                            continue;
                        }

                        int gid = data.TileIndices[tileIndex];

                        // Skip empty tiles (GID 0 or negative)
                        if (gid <= 0)
                        {
                            emptyTiles++;
                            continue;
                        }

                        // Calculate source rectangle for this tile
                        Rectangle sourceRect;
                        try
                        {
                            sourceRect = _resourceManager.CalculateTilesetSourceRectangle(
                                data.TilesetId,
                                gid,
                                data.FirstGid
                            );
                        }
                        catch (Exception)
                        {
                            invalidGids++;
                            // Invalid source rectangle - skip tile (no logging needed, happens frequently)
                            continue;
                        }

                        // Calculate world position for this tile
                        Vector2 tilePosition = new Vector2(
                            pos.Position.X + x * tilesetDefinition.TileWidth,
                            pos.Position.Y + y * tilesetDefinition.TileHeight
                        );

                        // Draw the tile
                        _spriteBatch!.Draw(tilesetTexture, tilePosition, sourceRect, color);

                        tilesRendered++;
                    }
                }
            }
            else
            {
                // Slower path: check for animated tiles
                // CRITICAL: Check entity is alive before accessing components
                if (!World.IsAlive(chunkEntity))
                {
                    return 0;
                }

                // Defensive check: ensure component exists (should always exist if HasAnimatedTiles is true)
                if (!World.Has<AnimatedTileDataComponent>(chunkEntity))
                {
                    _logger.Warning(
                        "MapRendererSystem.RenderChunk: Chunk at ({X}, {Y}) has HasAnimatedTiles=true but missing AnimatedTileDataComponent",
                        pos.Position.X,
                        pos.Position.Y
                    );
                    return 0;
                }

                ref var animData = ref World.Get<AnimatedTileDataComponent>(chunkEntity);

                // Render each tile in the chunk
                for (int y = 0; y < chunk.ChunkHeight; y++)
                {
                    for (int x = 0; x < chunk.ChunkWidth; x++)
                    {
                        int tileIndex = y * chunk.ChunkWidth + x;
                        if (tileIndex >= data.TileIndices.Length)
                        {
                            continue;
                        }

                        int gid = data.TileIndices[tileIndex];

                        // Skip empty tiles (GID 0 or negative)
                        if (gid <= 0)
                        {
                            emptyTiles++;
                            continue;
                        }

                        // Determine render GID (use animated frame if this tile is animated)
                        int renderGid = gid;
                        if (
                            animData.AnimatedTiles != null
                            && animData.AnimatedTiles.TryGetValue(tileIndex, out var animState)
                        )
                        {
                            // Get animation frames from cache
                            var frames = _resourceManager.GetCachedTileAnimation(
                                animState.AnimationTilesetId,
                                animState.AnimationLocalTileId
                            );

                            if (
                                frames != null
                                && frames.Count > 0
                                && animState.CurrentFrameIndex < frames.Count
                            )
                            {
                                // Use current animation frame's tile ID
                                var currentFrame = frames[animState.CurrentFrameIndex];
                                renderGid = currentFrame.TileId + data.FirstGid;
                            }
                        }

                        // Calculate source rectangle for this tile
                        Rectangle sourceRect;
                        try
                        {
                            sourceRect = _resourceManager.CalculateTilesetSourceRectangle(
                                data.TilesetId,
                                renderGid,
                                data.FirstGid
                            );
                        }
                        catch (Exception)
                        {
                            invalidGids++;
                            // Invalid source rectangle - skip tile (no logging needed, happens frequently)
                            continue;
                        }

                        // Calculate world position for this tile
                        Vector2 tilePosition = new Vector2(
                            pos.Position.X + x * tilesetDefinition.TileWidth,
                            pos.Position.Y + y * tilesetDefinition.TileHeight
                        );

                        // Draw the tile
                        _spriteBatch!.Draw(tilesetTexture, tilePosition, sourceRect, color);

                        tilesRendered++;
                    }
                }
            }

            // Chunk rendered - no logging needed (empty chunks are normal, happens every frame)

            return tilesRendered;
        }
    }
}
