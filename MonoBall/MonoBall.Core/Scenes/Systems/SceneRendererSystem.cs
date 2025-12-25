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
    /// System responsible for coordinating scene rendering across all scene types.
    /// Handles priority ordering, BlocksDraw coordination, and delegates to scene-specific systems.
    /// </summary>
    public partial class SceneRendererSystem : BaseSystem<World, float>
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly SceneSystem _sceneSystem;
        private readonly GameSceneSystem _gameSceneSystem;
        private readonly LoadingSceneSystem _loadingSceneSystem;
        private readonly DebugBarSceneSystem _debugBarSceneSystem;
        private readonly ECS.Systems.MapPopupSystem _mapPopupSystem;
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private SpriteBatch? _spriteBatch;

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the SceneRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="sceneSystem">The scene system for accessing scene stack.</param>
        /// <param name="gameSceneSystem">The game scene system.</param>
        /// <param name="loadingSceneSystem">The loading scene system.</param>
        /// <param name="debugBarSceneSystem">The debug bar scene system.</param>
        /// <param name="mapPopupSystem">The map popup system.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="shaderManagerSystem">The shader manager system for combined layer shaders (optional).</param>
        public SceneRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SceneSystem sceneSystem,
            GameSceneSystem gameSceneSystem,
            LoadingSceneSystem loadingSceneSystem,
            DebugBarSceneSystem debugBarSceneSystem,
            ECS.Systems.MapPopupSystem mapPopupSystem,
            ILogger logger,
            ShaderManagerSystem? shaderManagerSystem = null
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _sceneSystem = sceneSystem ?? throw new ArgumentNullException(nameof(sceneSystem));
            _gameSceneSystem =
                gameSceneSystem ?? throw new ArgumentNullException(nameof(gameSceneSystem));
            _loadingSceneSystem =
                loadingSceneSystem ?? throw new ArgumentNullException(nameof(loadingSceneSystem));
            _debugBarSceneSystem =
                debugBarSceneSystem ?? throw new ArgumentNullException(nameof(debugBarSceneSystem));
            _mapPopupSystem =
                mapPopupSystem ?? throw new ArgumentNullException(nameof(mapPopupSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _shaderManagerSystem = shaderManagerSystem;
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
        /// Gets the background color for the current scene state.
        /// Determines color based on the highest priority active scene that blocks draw.
        /// Requires BackgroundColor to be set on SceneComponent.
        /// </summary>
        /// <returns>The background color to use for clearing the screen.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no active scene with BackgroundColor is found.</exception>
        public Color GetBackgroundColor()
        {
            Color? backgroundColor = null;

            // Iterate scenes in reverse order (lowest priority first, highest priority last)
            // Find the first active scene that blocks draw (this is what will be rendered)
            _sceneSystem.IterateScenesReverse(
                (sceneEntity, sceneComponent) =>
                {
                    // Skip inactive scenes
                    if (!sceneComponent.IsActive)
                    {
                        return true; // Continue iterating
                    }

                    // Require BackgroundColor to be set
                    if (!sceneComponent.BackgroundColor.HasValue)
                    {
                        _logger.Warning(
                            "Scene '{SceneId}' (entity {EntityId}) does not have BackgroundColor set. Scene must have BackgroundColor specified.",
                            sceneComponent.SceneId,
                            sceneEntity.Id
                        );
                        return true; // Continue iterating to find a scene with BackgroundColor
                    }

                    backgroundColor = sceneComponent.BackgroundColor.Value;

                    // If scene blocks draw, stop iterating
                    if (sceneComponent.BlocksDraw)
                    {
                        return false; // Stop iterating
                    }

                    return true; // Continue iterating
                }
            );

            // Fail fast if no scene with BackgroundColor found
            if (!backgroundColor.HasValue)
            {
                throw new InvalidOperationException(
                    "No active scene with BackgroundColor found. All scenes must have BackgroundColor specified."
                );
            }

            return backgroundColor.Value;
        }

        /// <summary>
        /// Renders all scenes in reverse priority order (lowest priority first, highest priority last).
        /// This ensures higher priority scenes render on top of lower priority scenes.
        /// Coordinator handles priority ordering and BlocksDraw coordination.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
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
            // Coordinator handles priority ordering and BlocksDraw coordination
            _sceneSystem.IterateScenesReverse(
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

                    // Determine scene type and call appropriate scene system
                    if (World.Has<GameSceneComponent>(sceneEntity))
                    {
                        _gameSceneSystem.RenderScene(sceneEntity, gameTime);
                    }
                    else if (World.Has<LoadingSceneComponent>(sceneEntity))
                    {
                        _loadingSceneSystem.RenderScene(sceneEntity, gameTime);
                    }
                    else if (World.Has<DebugBarSceneComponent>(sceneEntity))
                    {
                        _debugBarSceneSystem.RenderScene(sceneEntity, gameTime);
                    }
                    else if (World.Has<MapPopupSceneComponent>(sceneEntity))
                    {
                        _mapPopupSystem.RenderScene(sceneEntity, gameTime);
                    }

                    // Coordinator checks BlocksDraw - scene systems don't
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
