using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Systems;
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
        private SpriteBatch? _spriteBatch;
        private MapRendererSystem? _mapRendererSystem;

        /// <summary>
        /// Initializes a new instance of the SceneRendererSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="sceneManagerSystem">The scene manager system for accessing scene stack.</param>
        public SceneRendererSystem(
            World world,
            GraphicsDevice graphicsDevice,
            SceneManagerSystem sceneManagerSystem
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _sceneManagerSystem =
                sceneManagerSystem ?? throw new ArgumentNullException(nameof(sceneManagerSystem));
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
        /// Renders all scenes in priority order.
        /// </summary>
        /// <param name="gameTime">The game time.</param>
        public void Render(GameTime gameTime)
        {
            if (_spriteBatch == null)
            {
                Log.Warning("SceneRendererSystem.Render called but SpriteBatch is null");
                return;
            }

            // Iterate scenes using helper method from SceneManagerSystem
            _sceneManagerSystem.IterateScenes(
                (sceneEntity, sceneComponent) =>
                {
                    // Skip inactive scenes
                    if (!sceneComponent.IsActive)
                    {
                        return true; // Continue iterating
                    }

                    // Render the scene
                    RenderScene(sceneEntity, ref sceneComponent, gameTime);

                    // If scene blocks draw, stop iterating (lower scenes don't render)
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
                        var cameraQuery = new QueryDescription().WithAll<CameraComponent>();
                        World.Query(
                            in cameraQuery,
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
                            Log.Warning(
                                "SceneRendererSystem: Scene '{SceneId}' specified SceneCamera mode but camera entity {CameraEntityId} is not found or doesn't have CameraComponent",
                                scene.SceneId,
                                cameraEntityId
                            );
                            return;
                        }
                    }
                    else
                    {
                        Log.Warning(
                            "SceneRendererSystem: Scene '{SceneId}' specified SceneCamera mode but CameraEntityId is null",
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
                Log.Warning(
                    "SceneRendererSystem: Scene '{SceneId}' requires camera but none was found. Scene will not render.",
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

            var cameraQuery = new QueryDescription().WithAll<CameraComponent>();
            World.Query(
                in cameraQuery,
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
            // TODO: Add other scene types (PopupScene, UIScene, etc.) as they are implemented
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
            // TODO: Implement screen space rendering for UI scenes
            // For now, this is a placeholder
        }

        /// <summary>
        /// Renders a GameScene by calling MapRendererSystem to render all maps.
        /// Eventually will also render NPCs, player, and other game world entities.
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
                Log.Warning(
                    "SceneRendererSystem: Cannot render GameScene '{SceneId}' - MapRendererSystem is null",
                    scene.SceneId
                );
                return;
            }

            // Save original viewport
            var savedViewport = _graphicsDevice.Viewport;

            try
            {
                // Note: MapRendererSystem.Render() internally queries for the active camera
                // and applies the transform, sets viewport, etc. So we just call it directly.
                // The viewport management is handled inside MapRendererSystem.
                _mapRendererSystem.Render(gameTime);

                // TODO: Render NPCs, player, and other game world entities here
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
