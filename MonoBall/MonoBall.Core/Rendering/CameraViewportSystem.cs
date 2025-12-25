using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// System responsible for updating camera viewports when the window is resized.
    /// Handles integer scaling and aspect ratio preservation.
    /// </summary>
    public partial class CameraViewportSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly int _referenceWidth;
        private readonly int _referenceHeight;
        private readonly QueryDescription _cameraQuery;
        private readonly ILogger _logger;
        private int _lastWindowWidth;
        private int _lastWindowHeight;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.CameraViewport;

        /// <summary>
        /// Initializes a new instance of the CameraViewportSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="graphicsDevice">The graphics device.</param>
        /// <param name="referenceWidth">The reference width for aspect ratio (e.g., 240 for GBA).</param>
        /// <param name="referenceHeight">The reference height for aspect ratio (e.g., 160 for GBA).</param>
        /// <param name="logger">The logger for logging operations.</param>
        public CameraViewportSystem(
            World world,
            GraphicsDevice graphicsDevice,
            int referenceWidth = 240,
            int referenceHeight = 160,
            ILogger logger = null!
        )
            : base(world)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _referenceWidth = referenceWidth;
            _referenceHeight = referenceHeight;
            _cameraQuery = new QueryDescription().WithAll<CameraComponent>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lastWindowWidth = 0;
            _lastWindowHeight = 0;
        }

        /// <summary>
        /// Updates camera viewports for window resize.
        /// Only updates when window size actually changes (event-driven approach).
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            int windowWidth = _graphicsDevice.Viewport.Width;
            int windowHeight = _graphicsDevice.Viewport.Height;

            // Early return if window size is invalid
            if (windowWidth <= 0 || windowHeight <= 0)
            {
                return;
            }

            // Only update if window size has changed (event-driven approach)
            if (windowWidth == _lastWindowWidth && windowHeight == _lastWindowHeight)
            {
                return;
            }

            // Update cached window size
            _lastWindowWidth = windowWidth;
            _lastWindowHeight = windowHeight;

            // Update all active cameras
            World.Query(
                in _cameraQuery,
                (ref CameraComponent camera) =>
                {
                    if (camera.IsActive)
                    {
                        UpdateViewportForResize(
                            ref camera,
                            windowWidth,
                            windowHeight,
                            _referenceWidth,
                            _referenceHeight
                        );
                    }
                }
            );
        }

        /// <summary>
        /// Updates the camera viewport to maintain aspect ratio when the window is resized.
        /// Uses integer scaling based on a reference resolution to maintain pixel-perfect rendering.
        /// Can be called directly for initialization or by the system during updates.
        /// </summary>
        /// <param name="camera">The camera component to update.</param>
        /// <param name="windowWidth">The new window width.</param>
        /// <param name="windowHeight">The new window height.</param>
        /// <param name="referenceWidth">The reference width for aspect ratio (e.g., 240 for GBA).</param>
        /// <param name="referenceHeight">The reference height for aspect ratio (e.g., 160 for GBA).</param>
        public static void UpdateViewportForResize(
            ref CameraComponent camera,
            int windowWidth,
            int windowHeight,
            int referenceWidth,
            int referenceHeight
        )
        {
            // Validate reference dimensions to prevent division by zero
            if (referenceWidth <= 0 || referenceHeight <= 0)
            {
                throw new ArgumentException(
                    "Reference dimensions must be positive.",
                    nameof(referenceWidth)
                );
            }

            // Calculate the maximum integer scale from reference resolution that fits in the window
            int scaleX = Math.Max(1, windowWidth / referenceWidth);
            int scaleY = Math.Max(1, windowHeight / referenceHeight);

            // Use the smaller scale to ensure the entire viewport fits
            int scale = Math.Min(scaleX, scaleY);

            // Calculate viewport and virtual viewport dimensions using integer scale
            int viewportWidth = referenceWidth * scale;
            int viewportHeight = referenceHeight * scale;

            // Skip if viewport dimensions haven't changed (optimization)
            if (
                camera.Viewport.Width == viewportWidth
                && camera.Viewport.Height == viewportHeight
                && camera.VirtualViewport.Width == viewportWidth
                && camera.VirtualViewport.Height == viewportHeight
                && camera.ReferenceWidth == windowWidth
                && camera.ReferenceHeight == windowHeight
            )
            {
                return; // Viewport already matches, no need to recalculate
            }

            // Set Viewport to the integer reference multiple
            camera.Viewport = new Rectangle(0, 0, viewportWidth, viewportHeight);

            // VirtualViewport is centered in the window with letterboxing/pillarboxing
            // Use integer division to ensure even distribution of bars
            // If there's a remainder, it will be 1 pixel, which is acceptable for pixel-perfect rendering
            int virtualX = (windowWidth - viewportWidth) / 2;
            int virtualY = (windowHeight - viewportHeight) / 2;

            camera.VirtualViewport = new Rectangle(
                virtualX,
                virtualY,
                viewportWidth,
                viewportHeight
            );

            // On first resize (initialization), set zoom to match reference resolution
            // This ensures exactly referenceWidth/referenceHeight pixels are visible
            // Zoom = scale means: Viewport pixels / Zoom = reference pixels (e.g., 240x160)
            if (camera.ReferenceWidth == 0 || camera.ReferenceHeight == 0)
            {
                camera.ReferenceWidth = windowWidth;
                camera.ReferenceHeight = windowHeight;

                // Set zoom to the integer scale factor for pixel-perfect viewport
                // This ensures the viewport always shows the reference resolution in world space
                camera.Zoom = scale;
            }
            else
            {
                // On subsequent resizes, maintain the same world-view ratio
                int previousScaleX = Math.Max(1, camera.ReferenceWidth / referenceWidth);
                int previousScaleY = Math.Max(1, camera.ReferenceHeight / referenceHeight);
                int previousScale = Math.Min(previousScaleX, previousScaleY);

                // Scale zoom proportionally to maintain same world view
                if (previousScale > 0)
                {
                    float zoomRatio = (float)scale / previousScale;
                    camera.Zoom *= zoomRatio;
                }

                // Update reference dimensions
                camera.ReferenceWidth = windowWidth;
                camera.ReferenceHeight = windowHeight;
            }

            // Mark camera as dirty so transform matrix is recalculated
            camera.IsDirty = true;
        }
    }
}
