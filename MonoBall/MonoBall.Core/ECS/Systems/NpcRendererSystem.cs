using System;
using System.Collections.Generic;
using System.Linq;
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
    /// System responsible for rendering NPCs using SpriteBatch.
    /// </summary>
    public partial class NpcRendererSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ISpriteLoaderService _spriteLoader;
        private readonly ICameraService _cameraService;
        private SpriteBatch? _spriteBatch;
        private Viewport _savedViewport;
        private readonly QueryDescription _queryDescription;

        /// <summary>
        /// Initializes a new instance of the NpcRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device for rendering.</param>
        /// <param name="spriteLoader">The sprite loader service.</param>
        /// <param name="cameraService">The camera service for querying active camera.</param>
        public NpcRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            ISpriteLoaderService spriteLoader,
            ICameraService cameraService
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteLoader = spriteLoader ?? throw new ArgumentNullException(nameof(spriteLoader));
            _cameraService =
                cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _queryDescription = new QueryDescription().WithAll<
                NpcComponent,
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
        /// Renders all visible NPCs.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void Render(GameTime gameTime)
        {
            if (_spriteBatch == null)
            {
                Log.Warning("NpcRendererSystem.Render called but SpriteBatch is null");
                return;
            }

            // Get active camera
            CameraComponent? activeCamera = _cameraService.GetActiveCamera();
            if (!activeCamera.HasValue)
            {
                return;
            }

            var camera = activeCamera.Value;

            // Collect visible NPCs
            var npcs = CollectVisibleNpcs(camera);
            if (npcs.Count == 0)
            {
                return;
            }

            // Sort by render order
            npcs = SortNpcsByRenderOrder(npcs);

            // Render NPCs
            RenderNpcBatch(npcs, camera);
        }

        /// <summary>
        /// Collects visible NPCs within the camera's view bounds.
        /// </summary>
        /// <param name="camera">The active camera component.</param>
        /// <returns>List of visible NPCs with their components.</returns>
        private List<(
            Entity entity,
            NpcComponent npc,
            SpriteAnimationComponent anim,
            PositionComponent pos,
            RenderableComponent render
        )> CollectVisibleNpcs(CameraComponent camera)
        {
            // Get visible tile bounds from camera (in tile coordinates)
            Rectangle tileViewBounds = camera.GetTileViewBounds();

            // Convert to pixel bounds for culling NPCs (NPCs are positioned in pixels)
            Rectangle visiblePixelBounds = new Rectangle(
                tileViewBounds.X * camera.TileWidth,
                tileViewBounds.Y * camera.TileHeight,
                tileViewBounds.Width * camera.TileWidth,
                tileViewBounds.Height * camera.TileHeight
            );

            var npcs =
                new List<(
                    Entity entity,
                    NpcComponent npc,
                    SpriteAnimationComponent anim,
                    PositionComponent pos,
                    RenderableComponent render
                )>();

            // Single-pass query: get all components at once
            World.Query(
                in _queryDescription,
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

                    // Validate sprite definition exists
                    if (!_spriteLoader.ValidateSpriteDefinition(npc.SpriteId))
                    {
                        Log.Warning(
                            "NpcRendererSystem.CollectVisibleNpcs: Sprite definition not found for NPC {NpcId} (spriteId: {SpriteId})",
                            npc.NpcId,
                            npc.SpriteId
                        );
                        return;
                    }

                    // Get sprite definition for frame dimensions
                    var spriteDef = _spriteLoader.GetSpriteDefinition(npc.SpriteId);
                    if (spriteDef == null)
                    {
                        // Should not happen after validation, but defensive check
                        return;
                    }

                    // Cull NPCs outside visible bounds
                    Rectangle npcBounds = new Rectangle(
                        (int)pos.Position.X,
                        (int)pos.Position.Y,
                        spriteDef.FrameWidth,
                        spriteDef.FrameHeight
                    );

                    if (npcBounds.Intersects(visiblePixelBounds))
                    {
                        npcs.Add((entity, npc, anim, pos, render));
                    }
                }
            );

            return npcs;
        }

        /// <summary>
        /// Sorts NPCs by render order (elevation).
        /// </summary>
        /// <param name="npcs">The list of NPCs to sort.</param>
        /// <returns>The sorted list of NPCs.</returns>
        private List<(
            Entity entity,
            NpcComponent npc,
            SpriteAnimationComponent anim,
            PositionComponent pos,
            RenderableComponent render
        )> SortNpcsByRenderOrder(
            List<(
                Entity entity,
                NpcComponent npc,
                SpriteAnimationComponent anim,
                PositionComponent pos,
                RenderableComponent render
            )> npcs
        )
        {
            return npcs.OrderBy(n => n.render.RenderOrder).ToList();
        }

        /// <summary>
        /// Renders a batch of NPCs using SpriteBatch.
        /// </summary>
        /// <param name="npcs">The list of NPCs to render.</param>
        /// <param name="camera">The active camera component.</param>
        private void RenderNpcBatch(
            List<(
                Entity entity,
                NpcComponent npc,
                SpriteAnimationComponent anim,
                PositionComponent pos,
                RenderableComponent render
            )> npcs,
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

                // Begin sprite batch (null check already done in Render method)
                if (_spriteBatch == null)
                {
                    return;
                }

                _spriteBatch.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    null,
                    null,
                    null,
                    transform
                );

                // Render each NPC
                foreach (var (entity, npc, anim, pos, render) in npcs)
                {
                    RenderSingleNpc(npc, anim, pos, render);
                }

                _spriteBatch.End();
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
        /// Renders a single NPC.
        /// </summary>
        /// <param name="npc">The NPC component.</param>
        /// <param name="anim">The sprite animation component.</param>
        /// <param name="pos">The position component.</param>
        /// <param name="render">The renderable component.</param>
        private void RenderSingleNpc(
            NpcComponent npc,
            SpriteAnimationComponent anim,
            PositionComponent pos,
            RenderableComponent render
        )
        {
            // Get sprite texture
            var spriteTexture = _spriteLoader.GetSpriteTexture(npc.SpriteId);
            if (spriteTexture == null)
            {
                Log.Warning(
                    "NpcRendererSystem.RenderSingleNpc: Failed to get sprite texture for {SpriteId} (NPC {NpcId})",
                    npc.SpriteId,
                    npc.NpcId
                );
                return;
            }

            // Get current frame rectangle
            var frameRect = _spriteLoader.GetAnimationFrameRectangle(
                npc.SpriteId,
                anim.CurrentAnimationName,
                anim.CurrentFrameIndex
            );
            if (!frameRect.HasValue)
            {
                Log.Warning(
                    "NpcRendererSystem.RenderSingleNpc: Failed to get frame rectangle for sprite {SpriteId}, animation {AnimationName}, frame {FrameIndex}",
                    npc.SpriteId,
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

            // Draw the NPC (null check already done in RenderNpcBatch)
            if (_spriteBatch == null)
            {
                return;
            }

            _spriteBatch.Draw(
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
