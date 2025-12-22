using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Maps;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for rendering sprites (NPCs and Players) using SpriteBatch.
    /// </summary>
    public class SpriteRendererSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ISpriteLoaderService _spriteLoader;
        private readonly ICameraService _cameraService;
        private SpriteBatch? _spriteBatch;
        private Viewport _savedViewport;
        private readonly QueryDescription _npcQuery;
        private readonly QueryDescription _playerQuery;
        private PerformanceStatsSystem? _performanceStatsSystem;

        // Reusable collections to avoid allocations in hot paths
        private readonly List<(
            Entity entity,
            string spriteId,
            SpriteAnimationComponent anim,
            PositionComponent pos,
            RenderableComponent render
        )> _spriteList =
            new List<(
                Entity entity,
                string spriteId,
                SpriteAnimationComponent anim,
                PositionComponent pos,
                RenderableComponent render
            )>();

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the SpriteRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device for rendering.</param>
        /// <param name="spriteLoader">The sprite loader service.</param>
        /// <param name="cameraService">The camera service for querying active camera.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public SpriteRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            ISpriteLoaderService spriteLoader,
            ICameraService cameraService,
            ILogger logger
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteLoader = spriteLoader ?? throw new ArgumentNullException(nameof(spriteLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cameraService =
                cameraService ?? throw new ArgumentNullException(nameof(cameraService));

            // Separate queries for NPCs and Players (avoid World.Has<> checks in hot path)
            _npcQuery = new QueryDescription().WithAll<
                NpcComponent,
                SpriteAnimationComponent,
                PositionComponent,
                RenderableComponent
            >();

            _playerQuery = new QueryDescription().WithAll<
                PlayerComponent,
                SpriteSheetComponent,
                SpriteAnimationComponent,
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
        /// Renders all visible sprites (NPCs and Players).
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void Render(GameTime gameTime)
        {
            if (_spriteBatch == null)
            {
                _logger.Warning("SpriteRendererSystem.Render called but SpriteBatch is null");
                return;
            }

            // Get active camera
            CameraComponent? activeCamera = _cameraService.GetActiveCamera();
            if (!activeCamera.HasValue)
            {
                return;
            }

            var camera = activeCamera.Value;

            // Collect visible sprites
            CollectVisibleSprites(camera, _spriteList);
            if (_spriteList.Count == 0)
            {
                return;
            }

            // Sort by render order (in-place sort to avoid allocation)
            _spriteList.Sort((a, b) => a.render.RenderOrder.CompareTo(b.render.RenderOrder));

            _logger.Debug(
                "Rendering {SpriteCount} sprites (NPCs and Players) within camera view",
                _spriteList.Count
            );

            // Render sprites
            RenderSpriteBatch(_spriteList, camera);
        }

        /// <summary>
        /// Collects visible sprites (NPCs and Players) within the camera's view bounds.
        /// </summary>
        /// <param name="camera">The active camera component.</param>
        /// <param name="outputList">The list to populate with visible sprites (will be cleared first).</param>
        private void CollectVisibleSprites(
            CameraComponent camera,
            List<(
                Entity entity,
                string spriteId,
                SpriteAnimationComponent anim,
                PositionComponent pos,
                RenderableComponent render
            )> outputList
        )
        {
            // Clear the output list
            outputList.Clear();

            // Get visible tile bounds from camera (in tile coordinates)
            Rectangle tileViewBounds = camera.GetTileViewBounds();

            // Convert to pixel bounds for culling sprites (sprites are positioned in pixels)
            Rectangle visiblePixelBounds = new Rectangle(
                tileViewBounds.X * camera.TileWidth,
                tileViewBounds.Y * camera.TileHeight,
                tileViewBounds.Width * camera.TileWidth,
                tileViewBounds.Height * camera.TileHeight
            );

            // Query NPCs
            World.Query(
                in _npcQuery,
                (
                    Entity entity,
                    ref NpcComponent npc,
                    ref SpriteAnimationComponent anim,
                    ref PositionComponent pos,
                    ref RenderableComponent render
                ) =>
                {
                    if (!render.IsVisible)
                    {
                        return;
                    }

                    // Get sprite definition for frame dimensions
                    // Note: Sprite definitions are validated at entity creation time, so we assume they exist here
                    // However, we still check for null as a safety measure (e.g., if sprite definitions are removed after entity creation)
                    var spriteDef = _spriteLoader.GetSpriteDefinition(npc.SpriteId);
                    if (spriteDef == null)
                    {
                        return; // Skip rendering if sprite definition is missing (should not happen in normal operation)
                    }

                    // Cull NPCs outside visible bounds
                    Rectangle spriteBounds = new Rectangle(
                        (int)pos.Position.X,
                        (int)pos.Position.Y,
                        spriteDef.FrameWidth,
                        spriteDef.FrameHeight
                    );

                    if (spriteBounds.Intersects(visiblePixelBounds))
                    {
                        outputList.Add((entity, npc.SpriteId, anim, pos, render));
                    }
                }
            );

            // Query Players
            World.Query(
                in _playerQuery,
                (
                    Entity entity,
                    ref PlayerComponent player,
                    ref SpriteSheetComponent spriteSheet,
                    ref SpriteAnimationComponent anim,
                    ref PositionComponent pos,
                    ref RenderableComponent render
                ) =>
                {
                    if (!render.IsVisible)
                    {
                        return;
                    }

                    // Get sprite definition for frame dimensions
                    // Note: Sprite definitions are validated at entity creation time, so we assume they exist here
                    // However, we still check for null as a safety measure (e.g., if sprite definitions are removed after entity creation)
                    var spriteDef = _spriteLoader.GetSpriteDefinition(
                        spriteSheet.CurrentSpriteSheetId
                    );
                    if (spriteDef == null)
                    {
                        return; // Skip rendering if sprite definition is missing (should not happen in normal operation)
                    }

                    // Cull players outside visible bounds
                    Rectangle spriteBounds = new Rectangle(
                        (int)pos.Position.X,
                        (int)pos.Position.Y,
                        spriteDef.FrameWidth,
                        spriteDef.FrameHeight
                    );

                    if (spriteBounds.Intersects(visiblePixelBounds))
                    {
                        outputList.Add(
                            (entity, spriteSheet.CurrentSpriteSheetId, anim, pos, render)
                        );
                    }
                }
            );
        }

        /// <summary>
        /// Renders a batch of sprites using SpriteBatch.
        /// </summary>
        /// <param name="sprites">The list of sprites to render.</param>
        /// <param name="camera">The active camera component.</param>
        private void RenderSpriteBatch(
            List<(
                Entity entity,
                string spriteId,
                SpriteAnimationComponent anim,
                PositionComponent pos,
                RenderableComponent render
            )> sprites,
            CameraComponent camera
        )
        {
            // Save original viewport inside try block for safety
            try
            {
                _savedViewport = _graphicsDevice.Viewport;

                SetupRenderViewport(camera);

                // Get camera transform matrix (handles tile-to-pixel conversion, zoom, and centering)
                Matrix transform = camera.GetTransformMatrix();

                // Begin sprite batch
                // Note: _spriteBatch is already validated in Render() method before calling this method
                _spriteBatch!.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    null,
                    null,
                    null,
                    transform
                );

                // Render each sprite
                foreach (var (entity, spriteId, anim, pos, render) in sprites)
                {
                    RenderSingleSprite(spriteId, anim, pos, render);
                }

                _spriteBatch.End();

                // Increment draw call counter
                _performanceStatsSystem?.IncrementDrawCalls();
            }
            finally
            {
                // Restore original viewport
                _graphicsDevice.Viewport = _savedViewport;
            }
        }

        /// <summary>
        /// Sets up the render viewport based on camera settings.
        /// </summary>
        /// <param name="camera">The active camera component.</param>
        private void SetupRenderViewport(CameraComponent camera)
        {
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
        }

        /// <summary>
        /// Renders a single sprite.
        /// </summary>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="anim">The sprite animation component.</param>
        /// <param name="pos">The position component.</param>
        /// <param name="render">The renderable component.</param>
        private void RenderSingleSprite(
            string spriteId,
            SpriteAnimationComponent anim,
            PositionComponent pos,
            RenderableComponent render
        )
        {
            // Get sprite texture
            var spriteTexture = _spriteLoader.GetSpriteTexture(spriteId);
            if (spriteTexture == null)
            {
                _logger.Warning(
                    "SpriteRendererSystem.RenderSingleSprite: Failed to get sprite texture for {SpriteId}",
                    spriteId
                );
                return;
            }

            // Get current frame rectangle
            var frameRect = _spriteLoader.GetAnimationFrameRectangle(
                spriteId,
                anim.CurrentAnimationName,
                anim.CurrentFrameIndex
            );
            if (!frameRect.HasValue)
            {
                _logger.Warning(
                    "SpriteRendererSystem.RenderSingleSprite: Failed to get frame rectangle for sprite {SpriteId}, animation {AnimationName}, frame {FrameIndex}",
                    spriteId,
                    anim.CurrentAnimationName,
                    anim.CurrentFrameIndex
                );
                return;
            }

            // Calculate color with opacity
            var color = Color.White * render.Opacity;

            // Determine sprite effects
            SpriteEffects spriteEffects = SpriteEffects.None;
            if (anim.FlipHorizontal)
            {
                spriteEffects |= SpriteEffects.FlipHorizontally;
            }

            // Draw the sprite
            // Note: _spriteBatch is already validated in Render() method before calling RenderSpriteBatch()
            _spriteBatch!.Draw(
                spriteTexture,
                pos.Position,
                frameRect.Value,
                color,
                0.0f,
                Vector2.Zero,
                1.0f,
                spriteEffects,
                0.0f
            );
        }
    }
}
