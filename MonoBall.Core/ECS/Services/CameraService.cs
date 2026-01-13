using System;
using Arch.Core;
using Arch.Relationships;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using MonoBall.Core.Scenes.Relationships;
using Serilog;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service implementation for querying camera components from the ECS world.
/// </summary>
public class CameraService : ICameraService
{
    private static readonly QueryDescription CameraQueryDescription =
        new QueryDescription().WithAll<CameraComponent>();

    private readonly ILogger _logger;
    private readonly World _world;

    /// <summary>
    ///     Initializes a new instance of the CameraService.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public CameraService(World world, ILogger logger)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the active camera component.
    /// </summary>
    /// <returns>The active camera component, or null if not found.</returns>
    public CameraComponent? GetActiveCamera()
    {
        CameraComponent? activeCamera = null;
        _world.Query(
            in CameraQueryDescription,
            (ref CameraComponent camera) =>
            {
                if (camera.IsActive)
                    activeCamera = camera;
            }
        );

        return activeCamera;
    }

    /// <summary>
    ///     Gets the camera component for a scene entity based on its CameraMode.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <returns>
    ///     The camera component, or null if:
    ///     - Scene uses ScreenCamera mode (doesn't use a camera component)
    ///     - Scene uses GameCamera mode but no active camera exists
    ///     - Scene uses SceneCamera mode but no camera relationship exists
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when sceneEntity is not alive or does not have SceneComponent.
    /// </exception>
    public CameraComponent? GetCameraForScene(Entity sceneEntity)
    {
        if (!_world.IsAlive(sceneEntity))
            throw new ArgumentException($"Scene entity {sceneEntity.Id} is not alive.", nameof(sceneEntity));

        if (!_world.Has<SceneComponent>(sceneEntity))
            throw new ArgumentException($"Entity {sceneEntity.Id} does not have SceneComponent.", nameof(sceneEntity));

        ref var scene = ref _world.Get<SceneComponent>(sceneEntity);

        switch (scene.CameraMode)
        {
            case SceneCameraMode.GameCamera:
                return GetActiveCamera();

            case SceneCameraMode.SceneCamera:
                // Query via relationship
                var cameraEntity = GetCameraEntityForScene(sceneEntity);
                if (!cameraEntity.HasValue || !_world.IsAlive(cameraEntity.Value))
                    return null;

                if (!_world.Has<CameraComponent>(cameraEntity.Value))
                    return null;

                return _world.Get<CameraComponent>(cameraEntity.Value);

            case SceneCameraMode.ScreenCamera:
                return null; // ScreenCamera doesn't use a camera component

            default:
                return null;
        }
    }

    /// <summary>
    ///     Gets the camera entity for a scene via relationship query.
    /// </summary>
    /// <param name="sceneEntity">The scene entity.</param>
    /// <returns>
    ///     The camera entity, or null if:
    ///     - Scene uses ScreenCamera or GameCamera mode (no relationship)
    ///     - Scene uses SceneCamera mode but no relationship exists yet
    ///     - Entity is not alive or missing SceneComponent (returns null, not an error)
    /// </returns>
    /// <remarks>
    ///     Returns null for valid states (ScreenCamera, GameCamera, or SceneCamera without relationship).
    ///     Does not throw exceptions for missing relationships - this is expected during scene creation.
    /// </remarks>
    public Entity? GetCameraEntityForScene(Entity sceneEntity)
    {
        // Validate entity is alive before querying relationships
        if (!_world.IsAlive(sceneEntity))
            return null; // Entity not alive - valid state, not an error

        // Additional validation: ensure entity has SceneComponent (sanity check)
        if (!_world.Has<SceneComponent>(sceneEntity))
            return null; // Missing SceneComponent - valid state during entity creation

        try
        {
            var relationships = _world.GetRelationships<UsesCamera>(sceneEntity);
            if (relationships == null)
                return null;

            // Return first camera entity (scenes should only have one camera)
            // Iterate through relationships to find the camera entity
            foreach (var kvp in relationships)
            {
                var cameraEntity = kvp.Key;
                if (_world.IsAlive(cameraEntity))
                    return cameraEntity;
            }

            return null;
        }
        catch (InvalidOperationException)
        {
            // Relationship query failed - skip this scene
            // InvalidOperationException is thrown by Arch.Extended when relationship queries fail
            return null;
        }
        catch (ArgumentException)
        {
            // Invalid entity or relationship type
            return null;
        }
    }
}
