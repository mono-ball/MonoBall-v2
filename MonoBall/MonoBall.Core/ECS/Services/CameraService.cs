using System;
using Arch.Core;
using MonoBall.Core.ECS.Components;
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
}
