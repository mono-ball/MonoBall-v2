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
    /// System responsible for rendering sprites (NPCs and Players) using SpriteBatch.
    /// </summary>
    public class SpriteRendererSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly IResourceManager _resourceManager;
        private readonly ICameraService _cameraService;
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private readonly ShaderRendererSystem? _shaderRendererSystem;
        private readonly RenderTargetManager? _renderTargetManager;
        private readonly IShaderService? _shaderService;
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
        /// <param name="resourceManager">The resource manager for loading sprite textures and definitions.</param>
        /// <param name="cameraService">The camera service for querying active camera.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="shaderManagerSystem">The shader manager system for sprite layer shaders (optional).</param>
        /// <param name="shaderService">The shader service for per-entity shaders (optional).</param>
        /// <param name="shaderRendererSystem">The shader renderer system for shader stacking (optional).</param>
        /// <param name="renderTargetManager">The render target manager for shader stacking (optional).</param>
        public SpriteRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            IResourceManager resourceManager,
            ICameraService cameraService,
            ILogger logger,
            ShaderManagerSystem? shaderManagerSystem = null,
            IShaderService? shaderService = null,
            ShaderRendererSystem? shaderRendererSystem = null,
            RenderTargetManager? renderTargetManager = null
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _resourceManager =
                resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cameraService =
                cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _shaderManagerSystem = shaderManagerSystem;
            _shaderService = shaderService;
            _shaderRendererSystem = shaderRendererSystem;
            _renderTargetManager = renderTargetManager;

            // Separate queries for NPCs and Players (avoid World.Has<> checks in hot path)
            // NPC query includes ActiveMapEntity tag to only process NPCs in active maps
            _npcQuery = new QueryDescription().WithAll<
                NpcComponent,
                SpriteAnimationComponent,
                PositionComponent,
                RenderableComponent,
                ActiveMapEntity
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
        /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, uses global shaders only.</param>
        public void Render(GameTime gameTime, Entity? sceneEntity = null)
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

            // Render sprites - no logging needed (this happens every frame)
            RenderSpriteBatch(_spriteList, camera, sceneEntity);
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

            // Expand bounds slightly to include edge sprites that might be partially visible
            // This prevents sprites from disappearing too early when moving near viewport edges
            int expandTiles = 1; // Expand by 1 tile in each direction
            Rectangle expandedTileBounds = new Rectangle(
                tileViewBounds.X - expandTiles,
                tileViewBounds.Y - expandTiles,
                tileViewBounds.Width + (expandTiles * 2),
                tileViewBounds.Height + (expandTiles * 2)
            );

            // Convert to pixel bounds for culling sprites (sprites are positioned in pixels)
            Rectangle visiblePixelBounds = new Rectangle(
                expandedTileBounds.X * camera.TileWidth,
                expandedTileBounds.Y * camera.TileHeight,
                expandedTileBounds.Width * camera.TileWidth,
                expandedTileBounds.Height * camera.TileHeight
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
                    var spriteDef = _resourceManager.GetSpriteDefinition(npc.SpriteId);
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
                    var spriteDef = _resourceManager.GetSpriteDefinition(
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
            CameraComponent camera,
            Entity? sceneEntity = null
        )
        {
            // Save original viewport inside try block for safety
            try
            {
                _savedViewport = _graphicsDevice.Viewport;

                SetupRenderViewport(camera);

                // Get camera transform matrix (handles tile-to-pixel conversion, zoom, and centering)
                Matrix transform = camera.GetTransformMatrix();

                // Get sprite layer shader stack (filtered by scene if provided)
                var shaderStack = _shaderManagerSystem?.GetSpriteLayerShaderStack(sceneEntity);
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
                        "SpriteRendererSystem: Shader stacking requested but ShaderRendererSystem or RenderTargetManager not available. Falling back to single shader."
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
                        renderTarget = _renderTargetManager!.GetOrCreateRenderTarget(101); // Use index 101 for sprite layer
                        if (renderTarget == null)
                        {
                            _logger.Warning(
                                "SpriteRendererSystem: Failed to create render target for shader stacking. Falling back to direct rendering."
                            );
                            needsShaderStacking = false;
                        }
                    }

                    if (needsShaderStacking && renderTarget != null)
                    {
                        // Render sprites with per-entity shaders to render target (layer shaders applied by ApplyShaderStack)
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
                            // Don't apply layer shader during geometry rendering - ApplyShaderStack will handle all layer shaders
                            // Only use per-entity shaders (if any)
                            // Sort sprites by shader to minimize SpriteBatch restarts
                            sprites.Sort(
                                (a, b) =>
                                {
                                    Effect? shaderA = GetEntityShader(a.entity);
                                    Effect? shaderB = GetEntityShader(b.entity);
                                    return (shaderA?.GetHashCode() ?? 0).CompareTo(
                                        shaderB?.GetHashCode() ?? 0
                                    );
                                }
                            );

                            // Render sprites with per-entity shader batching (no layer shader)
                            Effect? currentShader = null; // No layer shader - ApplyShaderStack will handle it
                            bool batchStarted = false;

                            foreach (var (entity, spriteId, anim, pos, render) in sprites)
                            {
                                // Check for per-entity shader only (no layer shader fallback)
                                Effect? entityShader = GetEntityShader(entity);
                                Effect? activeShader = entityShader; // No layer shader - ApplyShaderStack will handle it

                                // If shader changed, restart SpriteBatch
                                if (activeShader != currentShader)
                                {
                                    if (batchStarted)
                                    {
                                        _spriteBatch!.End();
                                    }

                                    currentShader = activeShader;
                                    batchStarted = true;

                                    // Ensure CurrentTechnique is set before using with SpriteBatch
                                    if (currentShader != null)
                                    {
                                        ShaderParameterApplier.EnsureCurrentTechnique(
                                            currentShader,
                                            _logger
                                        );
                                    }

                                    // Use Immediate mode for custom effects to ensure proper parameter application
                                    _spriteBatch!.Begin(
                                        SpriteSortMode.Immediate,
                                        BlendState.AlphaBlend,
                                        SamplerState.PointClamp,
                                        null,
                                        null,
                                        currentShader,
                                        transform
                                    );
                                }
                                else if (!batchStarted)
                                {
                                    // Start first batch
                                    batchStarted = true;

                                    // Ensure CurrentTechnique is set before using with SpriteBatch
                                    if (currentShader != null)
                                    {
                                        ShaderParameterApplier.EnsureCurrentTechnique(
                                            currentShader,
                                            _logger
                                        );
                                    }

                                    // Use Immediate mode for custom effects to ensure proper parameter application
                                    _spriteBatch!.Begin(
                                        SpriteSortMode.Immediate,
                                        BlendState.AlphaBlend,
                                        SamplerState.PointClamp,
                                        null,
                                        null,
                                        currentShader,
                                        transform
                                    );
                                }

                                RenderSingleSprite(spriteId, anim, pos, render);
                            }

                            if (batchStarted)
                            {
                                _spriteBatch!.End();
                            }

                            // Apply shader stack
                            _shaderRendererSystem!.ApplyShaderStack(
                                renderTarget,
                                null, // Render to back buffer
                                shaderStack!,
                                _spriteBatch!,
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
                    Effect? spriteLayerShader =
                        shaderStack != null && shaderStack.Count > 0 ? shaderStack[0].effect : null;

                    // Sort sprites by shader to minimize SpriteBatch restarts
                    sprites.Sort(
                        (a, b) =>
                        {
                            Effect? shaderA = GetEntityShader(a.entity);
                            Effect? shaderB = GetEntityShader(b.entity);
                            Effect? activeA = shaderA ?? spriteLayerShader;
                            Effect? activeB = shaderB ?? spriteLayerShader;
                            return (activeA?.GetHashCode() ?? 0).CompareTo(
                                activeB?.GetHashCode() ?? 0
                            );
                        }
                    );

                    // Render sprites with shader batching
                    Effect? currentShader = spriteLayerShader;
                    bool batchStarted = false;

                    foreach (var (entity, spriteId, anim, pos, render) in sprites)
                    {
                        // Check for per-entity shader
                        Effect? entityShader = GetEntityShader(entity);
                        Effect? activeShader = entityShader ?? spriteLayerShader;

                        // If shader changed, restart SpriteBatch
                        if (activeShader != currentShader)
                        {
                            if (batchStarted)
                            {
                                _spriteBatch!.End();
                            }

                            currentShader = activeShader;
                            batchStarted = true;

                            // Ensure CurrentTechnique is set before using with SpriteBatch
                            // MonoGame's SpriteBatch.Begin() will apply the effect automatically
                            if (currentShader != null)
                            {
                                ShaderParameterApplier.EnsureCurrentTechnique(
                                    currentShader,
                                    _logger
                                );
                            }

                            // Use Immediate mode for custom effects to ensure proper parameter application
                            _spriteBatch!.Begin(
                                SpriteSortMode.Immediate,
                                BlendState.AlphaBlend,
                                SamplerState.PointClamp,
                                null,
                                null,
                                currentShader,
                                transform
                            );
                        }
                        else if (!batchStarted)
                        {
                            // Start first batch
                            batchStarted = true;

                            // Ensure CurrentTechnique is set before using with SpriteBatch
                            // MonoGame's SpriteBatch.Begin() will apply the effect automatically
                            if (currentShader != null)
                            {
                                ShaderParameterApplier.EnsureCurrentTechnique(
                                    currentShader,
                                    _logger
                                );
                            }

                            // Use Immediate mode for custom effects to ensure proper parameter application
                            _spriteBatch!.Begin(
                                SpriteSortMode.Immediate,
                                BlendState.AlphaBlend,
                                SamplerState.PointClamp,
                                null,
                                null,
                                currentShader,
                                transform
                            );
                        }

                        RenderSingleSprite(spriteId, anim, pos, render);
                    }

                    if (batchStarted)
                    {
                        _spriteBatch!.End();
                    }

                    // Increment draw call counter
                    _performanceStatsSystem?.IncrementDrawCalls();
                }
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
        /// Gets the shader for an entity if it has a ShaderComponent.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>The shader effect, or null if no per-entity shader.</returns>
        private Effect? GetEntityShader(Entity entity)
        {
            if (_shaderService == null)
                return null;

            if (!World.Has<ShaderComponent>(entity))
                return null;

            ref var shaderComp = ref World.Get<ShaderComponent>(entity);
            if (!shaderComp.IsEnabled)
                return null;

            // Check if shader exists first (optional shader support)
            if (!_shaderService.HasShader(shaderComp.ShaderId))
            {
                _logger.Warning(
                    "Per-entity shader {ShaderId} not found, skipping",
                    shaderComp.ShaderId
                );
                return null;
            }

            // Load shader (fail fast if loading fails)
            Effect shader;
            try
            {
                shader = _shaderService.GetShader(shaderComp.ShaderId);
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "Failed to load per-entity shader {ShaderId}, skipping",
                    shaderComp.ShaderId
                );
                return null;
            }

            // Ensure CurrentTechnique is set
            ShaderParameterApplier.EnsureCurrentTechnique(shader, _logger);

            // Automatically set ScreenSize parameter if the shader has it
            // This prevents warnings about null ScreenSize parameters
            try
            {
                var screenSizeParam = shader.Parameters["ScreenSize"];
                if (
                    screenSizeParam != null
                    && screenSizeParam.ParameterClass == EffectParameterClass.Vector
                    && screenSizeParam.ColumnCount == 2
                )
                {
                    var viewport = _graphicsDevice.Viewport;
                    var screenSize = new Vector2(viewport.Width, viewport.Height);
                    screenSizeParam.SetValue(screenSize);
                }
            }
            catch (KeyNotFoundException)
            {
                // ScreenSize parameter doesn't exist - that's fine, not all shaders need it
            }
            catch (Exception ex)
            {
                // Log unexpected errors but don't fail - ScreenSize is optional
                _logger.Debug(
                    ex,
                    "Failed to set ScreenSize parameter automatically for per-entity shader"
                );
            }

            if (shaderComp.Parameters == null)
                return shader;

            // Apply shader parameters
            ApplyShaderParameters(shader, shaderComp.Parameters);
            return shader;
        }

        /// <summary>
        /// Applies shader parameters to an effect.
        /// </summary>
        /// <param name="effect">The shader effect.</param>
        /// <param name="parameters">The parameters dictionary.</param>
        private void ApplyShaderParameters(Effect effect, Dictionary<string, object> parameters)
        {
            // Use shared utility to avoid code duplication (DRY)
            ShaderParameterApplier.ApplyParameters(effect, parameters, _logger);
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
            Texture2D spriteTexture;
            try
            {
                spriteTexture = _resourceManager.LoadTexture(spriteId);
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
                    "SpriteRendererSystem.RenderSingleSprite: Failed to get sprite texture for {SpriteId}",
                    spriteId
                );
                return;
            }

            // Get current frame rectangle
            Rectangle frameRect;
            try
            {
                frameRect = _resourceManager.GetAnimationFrameRectangle(
                    spriteId,
                    anim.CurrentAnimationName,
                    anim.CurrentFrameIndex
                );
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    ex,
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
                frameRect,
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
