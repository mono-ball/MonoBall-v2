using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Systems;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that handles update and rendering for DebugBarScene entities.
    /// Queries for DebugBarSceneComponent entities and processes them.
    /// </summary>
    public class DebugBarSceneSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly DebugBarRendererSystem _debugBarRendererSystem;
        private readonly ILogger _logger;

        // Cached query descriptions to avoid allocations in hot paths
        private readonly QueryDescription _debugBarScenesQuery = new QueryDescription().WithAll<
            SceneComponent,
            DebugBarSceneComponent
        >();

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.DebugBarScene;

        /// <summary>
        /// Initializes a new instance of the DebugBarSceneSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="debugBarRendererSystem">The debug bar renderer system.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public DebugBarSceneSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            DebugBarRendererSystem debugBarRendererSystem,
            ILogger logger
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _debugBarRendererSystem =
                debugBarRendererSystem
                ?? throw new ArgumentNullException(nameof(debugBarRendererSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Updates active, unpaused debug bar scenes.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Query for active, unpaused debug bar scenes
            World.Query(
                in _debugBarScenesQuery,
                (Entity e, ref SceneComponent scene) =>
                {
                    if (scene.IsActive && !scene.IsPaused && !scene.BlocksUpdate)
                    {
                        // Debug bar scenes typically don't need per-frame updates
                        // But if they do, add logic here
                    }
                }
            );
        }

        /// <summary>
        /// Renders a single debug bar scene. Called by SceneRendererSystem (coordinator) for a single scene.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to render.</param>
        /// <param name="gameTime">The game time.</param>
        public void RenderScene(Entity sceneEntity, GameTime gameTime)
        {
            // Verify this is actually a debug bar scene
            if (!World.Has<DebugBarSceneComponent>(sceneEntity))
            {
                return;
            }

            ref var scene = ref World.Get<SceneComponent>(sceneEntity);
            if (!scene.IsActive)
            {
                return;
            }

            // DebugBarScene only supports screen-space rendering
            if (scene.CameraMode != SceneCameraMode.ScreenCamera)
            {
                _logger.Warning(
                    "DebugBarScene '{SceneId}' does not support camera-based rendering. Use ScreenCamera mode.",
                    scene.SceneId
                );
                return;
            }

            // Render the debug bar scene
            RenderDebugBarScene(sceneEntity, ref scene, gameTime);
        }

        /// <summary>
        /// Renders the debug bar scene in screen space.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderDebugBarScene(
            Entity sceneEntity,
            ref SceneComponent scene,
            GameTime gameTime
        )
        {
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
                _spriteBatch.Begin(
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
                    // Render debug bar
                    _debugBarRendererSystem.Render(gameTime);
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
    }
}
