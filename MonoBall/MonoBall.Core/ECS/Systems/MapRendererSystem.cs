using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.System;
using Arch.System.SourceGenerator;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Maps;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for rendering tile chunks using SpriteBatch.
    /// </summary>
    public partial class MapRendererSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ITilesetLoaderService _tilesetLoader;
        private SpriteBatch? _spriteBatch;
        private Viewport _savedViewport;

        /// <summary>
        /// Initializes a new instance of the MapRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device for rendering.</param>
        /// <param name="tilesetLoader">The tileset loader service.</param>
        public MapRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            ITilesetLoaderService tilesetLoader
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new System.ArgumentNullException(nameof(graphicsDevice));
            _tilesetLoader =
                tilesetLoader ?? throw new System.ArgumentNullException(nameof(tilesetLoader));
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
        /// Renders all visible tile chunks.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void Render(GameTime gameTime)
        {
            if (_spriteBatch == null)
            {
                Log.Warning("MapRendererSystem.Render called but SpriteBatch is null");
                return;
            }

            // Get active camera
            CameraComponent? activeCamera = GetActiveCamera();
            if (!activeCamera.HasValue)
            {
                Log.Debug("MapRendererSystem.Render: No active camera found, skipping render");
                return;
            }

            var camera = activeCamera.Value;

            // Get visible tile bounds from camera (in tile coordinates)
            Rectangle tileViewBounds = camera.GetTileViewBounds();

            // Convert to pixel bounds for culling chunks (chunks are positioned in pixels)
            Rectangle visiblePixelBounds = new Rectangle(
                tileViewBounds.X * camera.TileWidth,
                tileViewBounds.Y * camera.TileHeight,
                tileViewBounds.Width * camera.TileWidth,
                tileViewBounds.Height * camera.TileHeight
            );

            // Query all renderable tile chunks
            var queryDescription = new QueryDescription().WithAll<
                TileChunkComponent,
                TileDataComponent,
                PositionComponent,
                RenderableComponent
            >();

            var chunks =
                new List<(
                    Entity entity,
                    TileChunkComponent chunk,
                    TileDataComponent data,
                    PositionComponent pos,
                    RenderableComponent render
                )>();

            // Query entities first, then get components for each
            // This approach is needed because we require Entity references for optional component access
            var entities = new List<Entity>();
            World.Query(
                in queryDescription,
                (Entity entity) =>
                {
                    entities.Add(entity);
                }
            );

            // Get components for each entity and collect visible chunks
            foreach (var entity in entities)
            {
                ref var renderComp = ref World.Get<RenderableComponent>(entity);
                if (!renderComp.IsVisible)
                {
                    continue;
                }

                ref var chunkComp = ref World.Get<TileChunkComponent>(entity);
                ref var dataComp = ref World.Get<TileDataComponent>(entity);
                ref var posComp = ref World.Get<PositionComponent>(entity);

                // Get tile dimensions from tileset for accurate culling
                var tilesetDef = _tilesetLoader.GetTilesetDefinition(dataComp.TilesetId);
                int tileWidth = tilesetDef?.TileWidth ?? 16; // Default to 16 if not found
                int tileHeight = tilesetDef?.TileHeight ?? 16;

                // Cull chunks outside visible bounds (chunks are positioned in pixels)
                Rectangle chunkBounds = new Rectangle(
                    (int)posComp.Position.X,
                    (int)posComp.Position.Y,
                    chunkComp.ChunkWidth * tileWidth,
                    chunkComp.ChunkHeight * tileHeight
                );

                if (chunkBounds.Intersects(visiblePixelBounds))
                {
                    chunks.Add((entity, chunkComp, dataComp, posComp, renderComp));
                }
            }

            Log.Debug(
                "MapRendererSystem.Render: Found {ChunkCount} visible chunks to render (camera at {CameraX}, {CameraY})",
                chunks.Count,
                camera.Position.X,
                camera.Position.Y
            );

            if (chunks.Count == 0)
            {
                return;
            }

            // Sort by render order (layer index)
            chunks = chunks.OrderBy(c => c.chunk.LayerIndex).ThenBy(c => c.chunk.LayerId).ToList();

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

                // Render chunks
                _spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    null,
                    null,
                    null,
                    transform
                );

                int renderedChunks = 0;
                int renderedTiles = 0;
                foreach (var (entity, chunk, data, pos, render) in chunks)
                {
                    var tilesRendered = RenderChunk(entity, chunk, data, pos, render);
                    if (tilesRendered > 0)
                    {
                        renderedChunks++;
                        renderedTiles += tilesRendered;
                    }
                }

                _spriteBatch.End();

                if (renderedTiles > 0)
                {
                    Log.Debug(
                        "MapRendererSystem.Render: Rendered {ChunkCount} chunks with {TileCount} tiles",
                        renderedChunks,
                        renderedTiles
                    );
                }
            }
            finally
            {
                // Always restore original viewport, even if an exception occurred
                _graphicsDevice.Viewport = _savedViewport;
            }
        }

        /// <summary>
        /// Gets the active camera component.
        /// </summary>
        /// <returns>The active camera, or null if none found.</returns>
        private CameraComponent? GetActiveCamera()
        {
            CameraComponent? activeCamera = null;

            var cameraQuery = new QueryDescription().WithAll<CameraComponent>();
            World.Query(
                in cameraQuery,
                (ref CameraComponent camera) =>
                {
                    if (camera.IsActive)
                    {
                        activeCamera = camera;
                    }
                }
            );

            return activeCamera;
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
                Log.Debug(
                    "MapRendererSystem.RenderChunk: Chunk at ({X}, {Y}) is not visible",
                    pos.Position.X,
                    pos.Position.Y
                );
                return 0;
            }

            if (data.TileIndices == null)
            {
                Log.Warning(
                    "MapRendererSystem.RenderChunk: Chunk at ({X}, {Y}) has null TileIndices",
                    pos.Position.X,
                    pos.Position.Y
                );
                return 0;
            }

            // Get tileset texture
            var tilesetTexture = _tilesetLoader.GetTilesetTexture(data.TilesetId);
            if (tilesetTexture == null)
            {
                Log.Warning(
                    "MapRendererSystem.RenderChunk: Failed to get tileset texture for {TilesetId} at chunk ({X}, {Y})",
                    data.TilesetId,
                    pos.Position.X,
                    pos.Position.Y
                );
                return 0;
            }

            // Get tileset definition for tile dimensions
            var tilesetDefinition = _tilesetLoader.GetTilesetDefinition(data.TilesetId);
            if (tilesetDefinition == null)
            {
                Log.Warning(
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
                        var sourceRect = _tilesetLoader.CalculateSourceRectangle(
                            data.TilesetId,
                            gid,
                            data.FirstGid
                        );
                        if (sourceRect == null)
                        {
                            invalidGids++;
                            Log.Debug(
                                "MapRendererSystem.RenderChunk: Invalid source rectangle for GID {Gid} (tileset: {TilesetId}, firstGid: {FirstGid})",
                                gid,
                                data.TilesetId,
                                data.FirstGid
                            );
                            continue;
                        }

                        // Calculate world position for this tile
                        Vector2 tilePosition = new Vector2(
                            pos.Position.X + x * tilesetDefinition.TileWidth,
                            pos.Position.Y + y * tilesetDefinition.TileHeight
                        );

                        // Draw the tile
                        _spriteBatch!.Draw(tilesetTexture, tilePosition, sourceRect.Value, color);

                        tilesRendered++;
                    }
                }
            }
            else
            {
                // Slower path: check for animated tiles
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
                            var frames = _tilesetLoader.GetCachedAnimation(
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
                        var sourceRect = _tilesetLoader.CalculateSourceRectangle(
                            data.TilesetId,
                            renderGid,
                            data.FirstGid
                        );
                        if (sourceRect == null)
                        {
                            invalidGids++;
                            Log.Debug(
                                "MapRendererSystem.RenderChunk: Invalid source rectangle for GID {Gid} (tileset: {TilesetId}, firstGid: {FirstGid})",
                                renderGid,
                                data.TilesetId,
                                data.FirstGid
                            );
                            continue;
                        }

                        // Calculate world position for this tile
                        Vector2 tilePosition = new Vector2(
                            pos.Position.X + x * tilesetDefinition.TileWidth,
                            pos.Position.Y + y * tilesetDefinition.TileHeight
                        );

                        // Draw the tile
                        _spriteBatch!.Draw(tilesetTexture, tilePosition, sourceRect.Value, color);

                        tilesRendered++;
                    }
                }
            }

            if (tilesRendered == 0 && chunk.ChunkWidth * chunk.ChunkHeight > 0)
            {
                Log.Debug(
                    "MapRendererSystem.RenderChunk: Chunk at ({X}, {Y}) rendered 0 tiles (empty: {Empty}, invalid: {Invalid})",
                    pos.Position.X,
                    pos.Position.Y,
                    emptyTiles,
                    invalidGids
                );
            }

            return tilesRendered;
        }
    }
}
