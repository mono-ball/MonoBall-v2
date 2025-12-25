using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Systems;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that handles update and rendering for LoadingScene entities.
    /// Queries for LoadingSceneComponent entities and processes them.
    /// </summary>
    public class LoadingSceneSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SpriteBatch _spriteBatch;
        private readonly LoadingSceneRendererSystem _loadingSceneRendererSystem;
        private readonly ILogger _logger;

        // Cached query descriptions to avoid allocations in hot paths
        private readonly QueryDescription _loadingScenesQuery = new QueryDescription().WithAll<
            SceneComponent,
            LoadingSceneComponent
        >();

        /// <summary>
        /// Initializes a new instance of the LoadingSceneSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="loadingSceneRendererSystem">The loading scene renderer system.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public LoadingSceneSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SpriteBatch spriteBatch,
            LoadingSceneRendererSystem loadingSceneRendererSystem,
            ILogger logger
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
            _loadingSceneRendererSystem =
                loadingSceneRendererSystem
                ?? throw new ArgumentNullException(nameof(loadingSceneRendererSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Updates active, unpaused loading scenes.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Query for active, unpaused loading scenes
            World.Query(
                in _loadingScenesQuery,
                (Entity e, ref SceneComponent scene) =>
                {
                    if (scene.IsActive && !scene.IsPaused && !scene.BlocksUpdate)
                    {
                        // Loading scenes typically don't need per-frame updates
                        // But if they do, add logic here
                    }
                }
            );
        }

        /// <summary>
        /// Renders a single loading scene. Called by SceneRendererSystem (coordinator) for a single scene.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to render.</param>
        /// <param name="gameTime">The game time.</param>
        public void RenderScene(Entity sceneEntity, GameTime gameTime)
        {
            // Verify this is actually a loading scene
            if (!World.Has<LoadingSceneComponent>(sceneEntity))
            {
                return;
            }

            ref var scene = ref World.Get<SceneComponent>(sceneEntity);
            if (!scene.IsActive)
            {
                return;
            }

            // LoadingScene only supports screen-space rendering
            if (scene.CameraMode != SceneCameraMode.ScreenCamera)
            {
                _logger.Warning(
                    "LoadingScene '{SceneId}' does not support camera-based rendering. Use ScreenCamera mode.",
                    scene.SceneId
                );
                return;
            }

            // Render the loading scene
            RenderLoadingScene(sceneEntity, ref scene, gameTime);
        }

        /// <summary>
        /// Renders the loading scene in screen space.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="gameTime">The game time.</param>
        private void RenderLoadingScene(
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
    }
}
