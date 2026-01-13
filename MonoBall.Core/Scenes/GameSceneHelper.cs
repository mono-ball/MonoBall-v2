using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.Scenes.Components;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Helper class for common GameScene operations.
/// </summary>
public static class GameSceneHelper
{
    /// <summary>
    ///     Creates a GameScene entity with SceneComponent (CameraMode = GameCamera) and GameSceneComponent marker.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="sceneId">The scene ID.</param>
    /// <param name="priority">The scene priority.</param>
    /// <param name="backgroundColor">The background color for the scene.</param>
    /// <returns>The created scene entity.</returns>
    /// <remarks>
    ///     Note: This helper creates the entity directly via World.Create() and does not register it with SceneSystem.
    ///     For proper scene management, use ISceneManager.CreateScene() instead.
    /// </remarks>
    public static Entity CreateGameScene(
        World world,
        string sceneId,
        int priority,
        Color backgroundColor
    )
    {
        var sceneComponent = new SceneComponent
        {
            SceneId = sceneId,
            Priority = priority,
            CameraMode = SceneCameraMode.GameCamera,
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
