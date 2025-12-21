using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Service for querying camera components from the ECS world.
    /// </summary>
    public interface ICameraService
    {
        /// <summary>
        /// Gets the active camera component.
        /// </summary>
        /// <returns>The active camera component, or null if not found.</returns>
        CameraComponent? GetActiveCamera();
    }
}
