using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System responsible for orchestrating scene rendering based on camera mode and scene type.
    /// </summary>
    public partial class SceneRendererSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SceneManagerSystem _sceneManagerSystem;
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private readonly ShaderRendererSystem? _shaderRendererSystem;
        private readonly RenderTargetManager? _renderTargetManager;
        private SpriteBatch? _spriteBatch;
        private MapRendererSystem? _mapRendererSystem;
        private SpriteRendererSystem? _spriteRendererSystem;
        private MapBorderRendererSystem? _mapBorderRendererSystem;
        private DebugBarRendererSystem? _debugBarRendererSystem;
        private MapPopupRendererSystem? _mapPopupRendererSystem;
        private LoadingSceneRendererSystem? _loadingSceneRendererSystem;

        // Cached query descriptions to avoid allocations in hot paths
        private readonly QueryDescription _cameraQuery =
            new QueryDescription().WithAll<CameraComponent>();

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the SceneRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="sceneManagerSystem">The scene manager system for accessing scene stack.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="shaderManagerSystem">The shader manager system for combined layer shaders (optional).</param>
        /// <param name="renderTargetManager">The render target manager for post-processing (optional).</param>
        /// <param name="shaderRendererSystem">The shader renderer system for shader stacking (optional).</param>
        public SceneRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SceneManagerSystem sceneManagerSystem,
            ILogger logger,
            ShaderManagerSystem? shaderManagerSystem = null,
            RenderTargetManager? renderTargetManager = null,
            ShaderRendererSystem? shaderRendererSystem = null
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _sceneManagerSystem =
                sceneManagerSystem ?? throw new ArgumentNullException(nameof(sceneManagerSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _shaderManagerSystem = shaderManagerSystem;
            _renderTargetManager = renderTargetManager;
            _shaderRendererSystem = shaderRendererSystem;
        }

        /// <summary>
        /// Sets the SpriteBatch instance to use for rendering.
        /// </summary>
        /// <param name="spriteBatch">The SpriteBatch instance.</param>
        public void SetSpriteBatch(SpriteBatch spriteBatch)
        {
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        }

        /// <summary>
        /// Sets the MapRendererSystem reference for GameScene rendering.
        /// </summary>
        /// <param name="mapRendererSystem">The MapRendererSystem instance.</param>
        public void SetMapRendererSystem(MapRendererSystem mapRendererSystem)
        {
            _mapRendererSystem =
                mapRendererSystem ?? throw new ArgumentNullException(nameof(mapRendererSystem));
        }

        /// <summary>
        /// Sets the SpriteRendererSystem reference for GameScene rendering.
        /// </summary>
        /// <param name="spriteRendererSystem">The SpriteRendererSystem instance.</param>
        public void SetSpriteRendererSystem(SpriteRendererSystem spriteRendererSystem)
        {
            _spriteRendererSystem =
                spriteRendererSystem
                ?? throw new ArgumentNullException(nameof(spriteRendererSystem));
        }

        /// <summary>
        /// Sets the MapBorderRendererSystem reference for GameScene rendering.
        /// </summary>
        /// <param name="mapBorderRendererSystem">The MapBorderRendererSystem instance.</param>
        public void SetMapBorderRendererSystem(MapBorderRendererSystem mapBorderRendererSystem)
        {
            _mapBorderRendererSystem =
                mapBorderRendererSystem
                ?? throw new ArgumentNullException(nameof(mapBorderRendererSystem));
        }

        /// <summary>
        /// Sets the DebugBarRendererSystem reference for screen-space rendering.
        /// </summary>
        /// <param name="debugBarRendererSystem">The DebugBarRendererSystem instance.</param>
        public void SetDebugBarRendererSystem(DebugBarRendererSystem debugBarRendererSystem)
        {
            _debugBarRendererSystem =
                debugBarRendererSystem
                ?? throw new ArgumentNullException(nameof(debugBarRendererSystem));
        }

        /// <summary>
        /// Sets the MapPopupRendererSystem reference for popup scene rendering.
        /// </summary>
        /// <param name="mapPopupRendererSystem">The MapPopupRendererSystem instance.</param>
        public void SetMapPopupRendererSystem(MapPopupRendererSystem mapPopupRendererSystem)
        {
            _mapPopupRendererSystem =
                mapPopupRendererSystem
                ?? throw new ArgumentNullException(nameof(mapPopupRendererSystem));
        }

        /// <summary>
        /// Sets the LoadingSceneRendererSystem reference for loading scene rendering.
        /// </summary>
        /// <param name="loadingSceneRendererSystem">The LoadingSceneRendererSystem instance.</param>
        public void SetLoadingSceneRendererSystem(
            LoadingSceneRendererSystem loadingSceneRendererSystem
        )
        {
            _loadingSceneRendererSystem =
                loadingSceneRendererSystem
                ?? throw new ArgumentNullException(nameof(loadingSceneRendererSystem));
        }

        /// <summary>
        /// Renders all scenes in reverse priority order (lowest priority first, highest priority last).
        /// This ensures higher priority scenes render on top of lower priority scenes.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        /// <summary>
        /// Gets the background color for the current scene state.
        /// Determines color based on the highest priority active scene that blocks draw.
        /// </summary>
        /// <returns>The background color to use for clearing the screen.</returns>
        public Color GetBackgroundColor()
        {
            Color? backgroundColor = null;

            // Iterate scenes in reverse order (lowest priority first, highest priority last)
            // Find the first active scene that blocks draw (this is what will be rendered)
            _sceneManagerSystem.IterateScenesReverse(
                (sceneEntity, sceneComponent) =>
                {
                    // Skip inactive scenes
                    if (!sceneComponent.IsActive)
                    {
                        return true; // Continue iterating
                    }

                    // Determine background color based on scene type
                    if (World.Has<LoadingSceneComponent>(sceneEntity))
                    {
                        // Loading screen uses light gray theme
                        backgroundColor = new Color(234, 234, 233);
                        return false; // Stop iterating - found the scene that will render
                    }
                    else if (World.Has<GameSceneComponent>(sceneEntity))
                    {
                        // Game scene uses black background
                        backgroundColor = Color.Black;
                        return false; // Stop iterating - found the scene that will render
                    }

                    // If scene blocks draw, stop iterating (even if we haven't found a known scene type)
                    if (sceneComponent.BlocksDraw)
                    {
                        // Default to black for unknown scene types that block draw
                        if (!backgroundColor.HasValue)
                        {
                            backgroundColor = Color.Black;
                        }
                        return false; // Stop iterating
                    }

                    return true; // Continue iterating
                }
            );

            // Default to black if no scene found
            return backgroundColor ?? Color.Black;
        }

        public void Render(GameTime gameTime)
        {
            if (_spriteBatch == null)
            {
                _logger.Warning("SceneRendererSystem.Render called but SpriteBatch is null");
                return;
            }

            // Update ScreenSize parameter for all active shaders (tile, sprite, and combined layers)
            var viewport = _graphicsDevice.Viewport;
            _shaderManagerSystem?.UpdateAllLayersScreenSize(viewport.Width, viewport.Height);

            // Iterate scenes in reverse order (lowest priority first, highest priority last)
            // This ensures higher priority scenes render on top
            _sceneManagerSystem.IterateScenesReverse(
                (sceneEntity, sceneComponent) =>
                {
                    // Skip inactive scenes
                    if (!sceneComponent.IsActive)
                    {
                        return true; // Continue iterating
                    }

                    // Update shader state for this specific scene (critical timing fix)
                    // This ensures per-scene shaders are loaded before rendering
                    _shaderManagerSystem?.UpdateShaderState(sceneEntity);

                    // Render the scene
                    RenderScene(sceneEntity, ref sceneComponent, gameTime);

                    // If scene blocks draw, stop iterating
                    // Note: We iterate in reverse (lowest to highest priority), so if a scene blocks draw,
                    // it prevents higher priority scenes (that would render on top) from rendering.
                    // This allows lower priority scenes to fully occlude higher priority scenes when needed.
                    if (sceneComponent.BlocksDraw)
                    {
                        return false; // Stop iterating
                    }

                    return true; // Continue iterating
                }
            );
        }

        /// <summary>
        /// Renders a single scene based on its camera mode and scene type.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime)
        {
            CameraComponent? camera = null;

            // Determine camera based on CameraMode
            switch (scene.CameraMode)
            {
                case SceneCameraMode.GameCamera:
                    camera = GetActiveGameCamera();
                    break;

                case SceneCameraMode.ScreenCamera:
                    // No camera needed for screen space rendering
                    RenderScreenSpace(sceneEntity, ref scene, gameTime);
                    return;

                case SceneCameraMode.SceneCamera:
                    if (scene.CameraEntityId.HasValue)
                    {
                        // Query for camera entity by ID
                        // Capture the camera entity ID to avoid ref parameter issues in lambda
                        int cameraEntityId = scene.CameraEntityId.Value;
                        bool foundCamera = false;
                        World.Query(
                            in _cameraQuery,
                            (Entity entity, ref CameraComponent cam) =>
                            {
                                if (entity.Id == cameraEntityId)
                                {
                                    camera = cam;
                                    foundCamera = true;
                                }
                            }
                        );

                        if (!foundCamera)
                        {
                            _logger.Warning(
                                "Scene '{SceneId}' specified SceneCamera mode but camera entity {CameraEntityId} is not found or doesn't have CameraComponent",
                                scene.SceneId,
                                cameraEntityId
                            );
                            return;
                        }
                    }
                    else
                    {
                        _logger.Warning(
                            "Scene '{SceneId}' specified SceneCamera mode but CameraEntityId is null",
                            scene.SceneId
                        );
                        return;
                    }
                    break;
            }

            if (camera.HasValue)
            {
                RenderWithCamera(sceneEntity, ref scene, camera.Value, gameTime);
            }
            else
            {
                _logger.Warning(
                    "Scene '{SceneId}' requires camera but none was found. Scene will not render.",
                    scene.SceneId
                );
                // Scene cannot render without a camera, so we skip it
                // This is expected behavior - scenes requiring cameras must have them available
            }
        }

        /// <summary>
        /// Gets the active game camera (CameraComponent.IsActive == true).
        /// </summary>
        /// <returns>The active camera component, or null if none found.</returns>
        private CameraComponent? GetActiveGameCamera()
        {
            CameraComponent? activeCamera = null;

            World.Query(
                in _cameraQuery,
                (Entity entity, ref CameraComponent camera) =>
                {
                    if (camera.IsActive)
                    {
                        activeCamera = camera;
                    }
                }
            );

            return activeCamera;
        }

        /// <summary>
        /// Renders a scene using the specified camera transform.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="camera">The camera component to use.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderWithCamera(
            Entity sceneEntity,
            ref SceneComponent scene,
            CameraComponent camera,
            GameTime gameTime
        )
        {
            // Check scene type components to determine what to render
            if (World.Has<GameSceneComponent>(sceneEntity))
            {
                ref var gameSceneComponent = ref World.Get<GameSceneComponent>(sceneEntity);
                RenderGameScene(sceneEntity, ref scene, ref gameSceneComponent, camera, gameTime);
            }
            else if (World.Has<MapPopupSceneComponent>(sceneEntity))
            {
                _logger.Debug(
                    "SceneRendererSystem: Rendering MapPopupScene '{SceneId}' (entity {EntityId})",
                    scene.SceneId,
                    sceneEntity.Id
                );
                RenderPopupScene(sceneEntity, ref scene, camera, gameTime);
            }
            // TODO: Add other scene types (UIScene, etc.) as they are implemented
        }

        /// <summary>
        /// Renders a scene in screen space (full window, no camera transform).
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderScreenSpace(
            Entity sceneEntity,
            ref SceneComponent scene,
            GameTime gameTime
        )
        {
            // Check if this is a debug bar scene
            if (World.Has<DebugBarSceneComponent>(sceneEntity))
            {
                if (_debugBarRendererSystem == null)
                {
                    _logger.Warning(
                        "Scene '{SceneId}' is a DebugBarScene but DebugBarRendererSystem is null",
                        scene.SceneId
                    );
                    return;
                }

                // Save original viewport
                var savedViewport = _graphicsDevice.Viewport;

                try
                {
                    // Set viewport to full window
                    _graphicsDevice.Viewport = new Viewport(
                        0,
                        0,
                        _graphicsDevice.Viewport.Width,
                        _graphicsDevice.Viewport.Height
                    );

                    // Begin SpriteBatch with identity matrix for screen-space rendering
                    _spriteBatch!.Begin(
                        SpriteSortMode.Deferred,
                        BlendState.AlphaBlend,
                        SamplerState.PointClamp,
                        DepthStencilState.None,
                        RasterizerState.CullCounterClockwise,
                        null,
                        Matrix.Identity
                    );

                    // Render debug bar
                    _debugBarRendererSystem.Render(gameTime);

                    // End SpriteBatch
                    _spriteBatch.End();
                }
                finally
                {
                    // Always restore viewport, even if rendering fails
                    _graphicsDevice.Viewport = savedViewport;
                }
            }
            // Check if this is a loading scene
            else if (World.Has<LoadingSceneComponent>(sceneEntity))
            {
                if (_loadingSceneRendererSystem == null)
                {
                    _logger.Warning(
                        "Scene '{SceneId}' is a LoadingScene but LoadingSceneRendererSystem is null",
                        scene.SceneId
                    );
                    return;
                }

                // Save original viewport
                var savedViewport = _graphicsDevice.Viewport;

                try
                {
                    // Set viewport to full window
                    _graphicsDevice.Viewport = new Viewport(
                        0,
                        0,
                        _graphicsDevice.Viewport.Width,
                        _graphicsDevice.Viewport.Height
                    );

                    // Begin SpriteBatch with identity matrix for screen-space rendering
                    _spriteBatch!.Begin(
                        SpriteSortMode.Deferred,
                        BlendState.AlphaBlend,
                        SamplerState.PointClamp,
                        DepthStencilState.None,
                        RasterizerState.CullCounterClockwise,
                        null,
                        Matrix.Identity
                    );

                    try
                    {
                        // Render loading screen
                        _loadingSceneRendererSystem.Render(gameTime);
                    }
                    finally
                    {
                        // Always End SpriteBatch, even if Render() throws an exception
                        _spriteBatch.End();
                    }
                }
                finally
                {
                    // Always restore viewport, even if rendering fails
                    _graphicsDevice.Viewport = savedViewport;
                }
            }
            // TODO: Add other screen-space scene types (PopupScene, UIScene, etc.) as they are implemented
        }

        /// <summary>
        /// Renders a GameScene by calling MapRendererSystem to render all maps,
        /// then SpriteRendererSystem to render NPCs and players.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="_">The game scene component (marker component, unused but required for type identification).</param>
        /// <param name="camera">The camera component to use.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderGameScene(
            Entity sceneEntity,
            ref SceneComponent scene,
            ref GameSceneComponent _,
            CameraComponent camera,
            GameTime gameTime
        )
        {
            if (_mapRendererSystem == null)
            {
                _logger.Warning(
                    "SceneRendererSystem: Cannot render GameScene '{SceneId}' - MapRendererSystem is null",
                    scene.SceneId
                );
                return;
            }

            // Check for combined layer shader stack (post-processing) for this specific scene
            var shaderStack = _shaderManagerSystem?.GetCombinedLayerShaderStack(sceneEntity);
            bool hasPostProcessing = shaderStack != null && shaderStack.Count > 0;

            // Debug logging
            if (hasPostProcessing && shaderStack != null)
            {
                _logger.Debug(
                    "SceneRendererSystem: Combined layer shader stack found: {Count} shaders",
                    shaderStack.Count
                );
            }
            else
            {
                _logger.Debug("SceneRendererSystem: No combined layer shaders active");
            }

            RenderTarget2D? renderTarget = null;
            Viewport? originalViewport = null;

            // Determine render target: use post-processing render target if shaders are active
            if (hasPostProcessing && _renderTargetManager != null)
            {
                // Render to post-processing render target
                renderTarget = _renderTargetManager.GetOrCreateRenderTarget();
                if (renderTarget != null)
                {
                    originalViewport = _graphicsDevice.Viewport;
                    _graphicsDevice.SetRenderTarget(renderTarget);
                    _graphicsDevice.Clear(Color.Transparent);
                }
            }

            // Save original viewport
            var savedViewport = _graphicsDevice.Viewport;

            try
            {
                // Note: MapRendererSystem.Render() internally queries for the active camera
                // and applies the transform, sets viewport, etc. So we just call it directly.
                // The viewport management is handled inside MapRendererSystem.
                // Pass sceneEntity so shaders can be filtered per-scene
                _mapRendererSystem.Render(gameTime, sceneEntity);

                // Render border bottom layer (after maps, before sprites)
                if (_mapBorderRendererSystem != null)
                {
                    _mapBorderRendererSystem.Render(gameTime);
                }

                // Render sprites (NPCs and Players) (after maps, so sprites appear on top)
                if (_spriteRendererSystem != null)
                {
                    // Note: SpriteRendererSystem.Render() internally queries for the active camera
                    // and applies the transform, sets viewport, etc. Similar to MapRendererSystem.
                    // Pass sceneEntity so shaders can be filtered per-scene
                    _spriteRendererSystem.Render(gameTime, sceneEntity);
                }

                // Render border top layer (after sprites, so borders appear on top)
                if (_mapBorderRendererSystem != null)
                {
                    _mapBorderRendererSystem.RenderTopLayer(gameTime);
                }

                // If we rendered to a render target, now apply post-processing shader stack
                if (renderTarget != null && hasPostProcessing && shaderStack != null)
                {
                    _logger.Debug(
                        "SceneRendererSystem: Applying post-processing shader stack. RenderTarget: {Width}x{Height}, ShaderCount: {Count}",
                        renderTarget.Width,
                        renderTarget.Height,
                        shaderStack.Count
                    );

                    // Restore original render target and viewport
                    _graphicsDevice.SetRenderTarget(null);
                    if (originalViewport.HasValue)
                    {
                        _graphicsDevice.Viewport = originalViewport.Value;
                    }

                    // Update dynamic parameters for all shaders in stack
                    var viewport = _graphicsDevice.Viewport;
                    _shaderManagerSystem?.UpdateCombinedLayerScreenSize(
                        viewport.Width,
                        viewport.Height
                    );
                    _shaderManagerSystem?.ForceUpdateCombinedLayerParameters();

                    // Apply shader stack using ShaderRendererSystem
                    if (_shaderRendererSystem != null && _spriteBatch != null)
                    {
                        _shaderRendererSystem.ApplyShaderStack(
                            renderTarget,
                            null, // Render to back buffer
                            shaderStack,
                            _spriteBatch,
                            _graphicsDevice,
                            _renderTargetManager
                        );
                        _logger.Debug("SceneRendererSystem: Post-processing shader stack applied");
                    }
                    else
                    {
                        _logger.Warning(
                            "SceneRendererSystem: ShaderRendererSystem or SpriteBatch not available. Cannot apply shader stack."
                        );
                    }
                }
                else if (hasPostProcessing && renderTarget == null)
                {
                    _logger.Warning(
                        "SceneRendererSystem: Combined shader stack is active but render target is null. RenderTargetManager may not be initialized."
                    );
                }
            }
            finally
            {
                // Always restore viewport and render target, even if rendering fails
                if (renderTarget != null)
                {
                    _graphicsDevice.SetRenderTarget(null);
                    if (originalViewport.HasValue)
                    {
                        _graphicsDevice.Viewport = originalViewport.Value;
                    }
                }
                else
                {
                    _graphicsDevice.Viewport = savedViewport;
                }
            }
        }

        /// <summary>
        /// Renders a MapPopupScene by calling MapPopupRendererSystem to render popups.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="camera">The camera component to use.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderPopupScene(
            Entity sceneEntity,
            ref SceneComponent scene,
            CameraComponent camera,
            GameTime gameTime
        )
        {
            if (_mapPopupRendererSystem == null)
            {
                _logger.Warning(
                    "SceneRendererSystem: Cannot render PopupScene '{SceneId}' - MapPopupRendererSystem is null",
                    scene.SceneId
                );
                return;
            }

            // Save original viewport
            var savedViewport = _graphicsDevice.Viewport;

            try
            {
                // Set viewport to camera's virtual viewport (if available) or regular viewport
                // Popups render in screen space within this viewport
                if (camera.VirtualViewport != Rectangle.Empty)
                {
                    _graphicsDevice.Viewport = new Viewport(camera.VirtualViewport);
                }

                // Render popups in SCREEN SPACE (not world space) - use Matrix.Identity
                // Map popups are UI overlays that should stay fixed on screen
                _spriteBatch!.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullCounterClockwise,
                    null,
                    Matrix.Identity // Screen space - no camera transform
                );

                // Render popup (renderer handles popup rendering in screen space)
                _mapPopupRendererSystem.Render(sceneEntity, camera, gameTime);

                // End SpriteBatch
                _spriteBatch.End();
            }
            finally
            {
                // Always restore viewport, even if rendering fails
                _graphicsDevice.Viewport = savedViewport;
            }
        }

        /// <summary>
        /// Update method required by BaseSystem, but rendering is done via Render().
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Rendering is done via Render() method called from Game.Draw()
            // This Update() method is a no-op for renderer system
        }
    }
}
