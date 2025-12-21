using System;
using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Service implementation for querying camera components from the ECS world.
    /// </summary>
    public class CameraService : ICameraService
    {
        private readonly World _world;
        private static readonly QueryDescription CameraQueryDescription =
            new QueryDescription().WithAll<CameraComponent>();

        /// <summary>
        /// Initializes a new instance of the CameraService.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        public CameraService(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>
        /// Gets the active camera component.
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
                    {
                        activeCamera = camera;
                    }
                }
            );

            return activeCamera;
        }
    }
}
