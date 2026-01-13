using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Scenes.Components;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Utility class for querying cameras based on scene camera mode.
///     Extracts common camera query logic to follow DRY principles.
/// </summary>
public static class SceneCameraHelper
{
    /// <summary>
    ///     Gets the camera component for a scene entity based on its CameraMode.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <param name="cameraService">The camera service for GameCamera mode.</param>
    /// <param name="cameraQuery">The cached camera query description (must be created in constructor).</param>
    /// <returns>The camera component, or null if not found or scene doesn't have SceneComponent.</returns>
    public static CameraComponent? GetCameraForScene(
        World world,
        Entity sceneEntity,
        ICameraService cameraService,
        QueryDescription cameraQuery
    )
    {
        if (!world.Has<SceneComponent>(sceneEntity))
            return null;

        ref var scene = ref world.Get<SceneComponent>(sceneEntity);

        return GetCameraForScene(world, ref scene, cameraService, cameraQuery);
    }

    /// <summary>
    ///     Gets the camera component for a scene based on its CameraMode.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="scene">The scene component.</param>
    /// <param name="cameraService">The camera service for GameCamera mode.</param>
    /// <param name="cameraQuery">The cached camera query description (must be created in constructor).</param>
    /// <returns>The camera component, or null if not found.</returns>
    public static CameraComponent? GetCameraForScene(
        World world,
        ref SceneComponent scene,
        ICameraService cameraService,
        QueryDescription cameraQuery
    )
    {
        switch (scene.CameraMode)
        {
            case SceneCameraMode.GameCamera:
                return cameraService.GetActiveCamera();

            case SceneCameraMode.SceneCamera:
                if (!scene.CameraEntityId.HasValue)
                    return null;

                var cameraEntityId = scene.CameraEntityId.Value;
                CameraComponent? camera = null;
                world.Query(
                    in cameraQuery,
                    (Entity entity, ref CameraComponent cam) =>
                    {
                        if (entity.Id == cameraEntityId)
                            camera = cam;
                    }
                );
                return camera;

            case SceneCameraMode.ScreenCamera:
                // ScreenCamera doesn't use a camera component
                return null;

            default:
                return null;
        }
    }
}
