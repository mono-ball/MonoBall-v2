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
    ///     Uses CameraService for centralized camera queries.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <param name="cameraService">The camera service for camera queries.</param>
    /// <returns>The camera component, or null if not found or scene doesn't have SceneComponent.</returns>
    public static CameraComponent? GetCameraForScene(
        World world,
        Entity sceneEntity,
        ICameraService cameraService
    )
    {
        // Delegate to CameraService (no need for cameraQuery parameter anymore)
        return cameraService.GetCameraForScene(sceneEntity);
    }

    /// <summary>
    ///     Gets the camera component for a scene based on its CameraMode.
    ///     For GameCamera mode, uses GetActiveCamera(). For SceneCamera mode, requires scene entity.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="scene">The scene component.</param>
    /// <param name="cameraService">The camera service for camera queries.</param>
    /// <returns>The camera component, or null if not found.</returns>
    /// <remarks>
    ///     For SceneCamera mode, this method cannot resolve the camera without the scene entity.
    ///     Use the entity-based overload instead.
    /// </remarks>
    public static CameraComponent? GetCameraForScene(
        World world,
        ref SceneComponent scene,
        ICameraService cameraService
    )
    {
        // For GameCamera mode, use GetActiveCamera()
        if (scene.CameraMode == SceneCameraMode.GameCamera)
            return cameraService.GetActiveCamera();

        // For SceneCamera mode, need scene entity to query relationship
        // Caller should use the entity-based overload
        if (scene.CameraMode == SceneCameraMode.SceneCamera)
            return null; // Cannot resolve without entity

        // ScreenCamera doesn't use a camera
        return null;
    }
}
