using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for querying camera components from the ECS world.
/// </summary>
public interface ICameraService
{
    /// <summary>
    ///     Gets the active camera component.
    /// </summary>
    /// <returns>The active camera component, or null if not found.</returns>
    CameraComponent? GetActiveCamera();
    
    /// <summary>
    ///     Gets the camera component for a scene entity based on its CameraMode.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <returns>The camera component, or null if not found or scene doesn't have SceneComponent.</returns>
    CameraComponent? GetCameraForScene(Entity sceneEntity);
    
    /// <summary>
    ///     Gets the camera entity for a scene via relationship query.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <returns>The camera entity, or null if not found or relationship doesn't exist.</returns>
    Entity? GetCameraEntityForScene(Entity sceneEntity);
}
