using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Rendering;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that handles update and rendering for GameScene entities.
    /// Queries for GameSceneComponent entities and processes them.
    /// </summary>
    public class GameSceneSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly MapRendererSystem _mapRendererSystem;
        private readonly SpriteRendererSystem? _spriteRendererSystem;
        private readonly MapBorderRendererSystem? _mapBorderRendererSystem;
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private readonly ShaderRendererSystem? _shaderRendererSystem;
        private readonly RenderTargetManager? _renderTargetManager;
        private readonly ILogger _logger;

        // Cached query descriptions to avoid allocations in hot paths
        private readonly QueryDescription _gameScenesQuery = new QueryDescription().WithAll<
            SceneComponent,
            GameSceneComponent
        >();

        private readonly QueryDescription _cameraQuery =
            new QueryDescription().WithAll<CameraComponent>();

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.GameScene;

        /// <summary>
        /// Initializes a new instance of the GameSceneSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="mapRendererSystem">The map renderer system.</param>
        /// <param name="spriteRendererSystem">The sprite renderer system (optional).</param>
        /// <param name="mapBorderRendererSystem">The map border renderer system (optional).</param>
        /// <param name="shaderManagerSystem">The shader manager system (optional).</param>
        /// <param name="shaderRendererSystem">The shader renderer system (optional).</param>
        /// <param name="renderTargetManager">The render target manager (optional).</param>
        /// <param name="logger">The logger for logging operations.</param>
        public GameSceneSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            MapRendererSystem mapRendererSystem,
            SpriteRendererSystem? spriteRendererSystem = null,
            MapBorderRendererSystem? mapBorderRendererSystem = null,
            ShaderManagerSystem? shaderManagerSystem = null,
            ShaderRendererSystem? shaderRendererSystem = null,
            RenderTargetManager? renderTargetManager = null,
            ILogger? logger = null
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _mapRendererSystem =
                mapRendererSystem ?? throw new ArgumentNullException(nameof(mapRendererSystem));
            _spriteRendererSystem = spriteRendererSystem;
            _mapBorderRendererSystem = mapBorderRendererSystem;
            _shaderManagerSystem = shaderManagerSystem;
            _shaderRendererSystem = shaderRendererSystem;
            _renderTargetManager = renderTargetManager;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Updates active, unpaused game scenes.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Query for active, unpaused game scenes
            World.Query(
                in _gameScenesQuery,
                (Entity e, ref SceneComponent scene) =>
                {
                    if (scene.IsActive && !scene.IsPaused && !scene.BlocksUpdate)
                    {
                        // Game scenes typically don't need per-frame updates
                        // But if they do, add logic here
                    }
                }
            );
        }

        /// <summary>
        /// Renders a single game scene. Called by SceneRendererSystem (coordinator) for a single scene.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to render.</param>
        /// <param name="gameTime">The game time.</param>
        public void RenderScene(Entity sceneEntity, GameTime gameTime)
        {
            // Verify this is actually a game scene
            if (!World.Has<GameSceneComponent>(sceneEntity))
            {
                return;
            }

            ref var scene = ref World.Get<SceneComponent>(sceneEntity);
            if (!scene.IsActive)
            {
                return;
            }

            // Determine camera based on CameraMode
            CameraComponent? camera = null;

            switch (scene.CameraMode)
            {
                case SceneCameraMode.GameCamera:
                    camera = GetActiveGameCamera();
                    break;

                case SceneCameraMode.SceneCamera:
                    if (scene.CameraEntityId.HasValue)
                    {
                        // Query for camera entity by ID
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
                                "GameScene '{SceneId}' specified SceneCamera mode but camera entity {CameraEntityId} is not found or doesn't have CameraComponent",
                                scene.SceneId,
                                cameraEntityId
                            );
                            return;
                        }
                    }
                    else
                    {
                        _logger.Warning(
                            "GameScene '{SceneId}' specified SceneCamera mode but CameraEntityId is null",
                            scene.SceneId
                        );
                        return;
                    }
                    break;

                case SceneCameraMode.ScreenCamera:
                    // GameScene does not support screen-space rendering
                    _logger.Warning(
                        "GameScene '{SceneId}' does not support screen-space rendering. Use GameCamera or SceneCamera mode.",
                        scene.SceneId
                    );
                    return;
            }

            if (!camera.HasValue)
            {
                _logger.Warning(
                    "GameScene '{SceneId}' requires camera but none was found. Scene will not render.",
                    scene.SceneId
                );
                return;
            }

            // Render the game scene
            RenderGameScene(sceneEntity, ref scene, gameTime, camera.Value);
        }

        /// <summary>
        /// Renders the game scene using the specified camera.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="gameTime">The game time.</param>
        /// <param name="camera">The camera component.</param>
        private void RenderGameScene(
            Entity sceneEntity,
            ref SceneComponent scene,
            GameTime gameTime,
            CameraComponent camera
        )
        {
            // Check for combined layer shader stack (post-processing) for this specific scene
            var shaderStack = _shaderManagerSystem?.GetCombinedLayerShaderStack(sceneEntity);
            bool hasPostProcessing = shaderStack != null && shaderStack.Count > 0;

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
                // Render maps (pass sceneEntity so shaders can be filtered per-scene)
                _mapRendererSystem.Render(gameTime, sceneEntity);

                // Render border bottom layer (after maps, before sprites)
                if (_mapBorderRendererSystem != null)
                {
                    _mapBorderRendererSystem.Render(gameTime);
                }

                // Render sprites (NPCs and Players) (after maps, so sprites appear on top)
                if (_spriteRendererSystem != null)
                {
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
                    if (_shaderRendererSystem != null)
                    {
                        _shaderRendererSystem.ApplyShaderStack(
                            renderTarget,
                            null, // Render to back buffer
                            shaderStack,
                            _spriteBatch,
                            _graphicsDevice,
                            _renderTargetManager
                        );
                    }
                    else
                    {
                        _logger.Warning(
                            "ShaderRendererSystem not available. Cannot apply shader stack."
                        );
                    }
                }
                else if (hasPostProcessing && renderTarget == null)
                {
                    _logger.Warning(
                        "Combined shader stack is active but render target is null. RenderTargetManager may not be initialized."
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
    }
}
