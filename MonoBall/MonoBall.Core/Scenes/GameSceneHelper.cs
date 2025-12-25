using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.Scenes.Components;

namespace MonoBall.Core.Scenes
{
    /// <summary>
    /// Helper class for common GameScene operations.
    /// </summary>
    public static class GameSceneHelper
    {
        /// <summary>
        /// Creates a GameScene entity with SceneComponent (CameraMode = GameCamera) and GameSceneComponent marker.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="sceneId">The scene ID.</param>
        /// <param name="priority">The scene priority.</param>
        /// <param name="backgroundColor">The background color for the scene.</param>
        /// <param name="cameraEntityId">Optional camera entity ID (ignored for GameCamera mode).</param>
        /// <returns>The created scene entity.</returns>
        public static Entity CreateGameScene(
            World world,
            string sceneId,
            int priority,
            Color backgroundColor,
            int? cameraEntityId = null
        )
        {
            var sceneComponent = new SceneComponent
            {
                SceneId = sceneId,
                Priority = priority,
                CameraMode = SceneCameraMode.GameCamera,
                CameraEntityId = cameraEntityId,
                BlocksUpdate = false,
                BlocksDraw = false,
                BlocksInput = false,
                IsActive = true,
                IsPaused = false,
                BackgroundColor = backgroundColor,
            };

            var gameSceneComponent = new GameSceneComponent();

            return world.Create(sceneComponent, gameSceneComponent);
        }
    }
}
