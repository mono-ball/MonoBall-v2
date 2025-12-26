using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scripting
{
    /// <summary>
    /// API for camera-related operations.
    /// </summary>
    public interface ICameraApi
    {
        /// <summary>
        /// Gets the active camera component.
        /// </summary>
        /// <returns>The active camera component, or null if not found.</returns>
        CameraComponent? GetActiveCamera();

        /// <summary>
        /// Gets the camera's position.
        /// </summary>
        /// <returns>The camera position, or Vector2.Zero if no active camera.</returns>
        Vector2 GetCameraPosition();

        /// <summary>
        /// Gets the camera's viewport size.
        /// </summary>
        /// <returns>The viewport size, or Vector2.Zero if no active camera.</returns>
        Vector2 GetCameraViewport();
    }
}
