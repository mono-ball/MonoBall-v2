using System;
using Microsoft.Xna.Framework.Graphics;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Manages RenderTarget2D lifecycle for post-processing effects.
    /// Handles creation, resizing, and disposal of render targets.
    /// </summary>
    public class RenderTargetManager : IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ILogger _logger;
        private RenderTarget2D? _sceneRenderTarget;
        private int _lastWidth;
        private int _lastHeight;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the RenderTargetManager.
        /// </summary>
        /// <param name="graphicsDevice">The graphics device for creating render targets.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public RenderTargetManager(GraphicsDevice graphicsDevice, ILogger logger)
        {
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _lastWidth = graphicsDevice.Viewport.Width;
            _lastHeight = graphicsDevice.Viewport.Height;
        }

        /// <summary>
        /// Gets or creates a render target matching the current viewport dimensions.
        /// Automatically recreates the render target if viewport size changes.
        /// </summary>
        /// <returns>The render target, or null if creation fails.</returns>
        public RenderTarget2D? GetOrCreateRenderTarget()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RenderTargetManager));

            int currentWidth = _graphicsDevice.Viewport.Width;
            int currentHeight = _graphicsDevice.Viewport.Height;

            // Check if we need to recreate the render target
            if (
                _sceneRenderTarget == null
                || currentWidth != _lastWidth
                || currentHeight != _lastHeight
            )
            {
                DisposeRenderTarget();

                try
                {
                    _sceneRenderTarget = new RenderTarget2D(
                        _graphicsDevice,
                        currentWidth,
                        currentHeight,
                        false,
                        SurfaceFormat.Color,
                        DepthFormat.None
                    );
                    _lastWidth = currentWidth;
                    _lastHeight = currentHeight;
                    _logger.Debug(
                        "Created render target: {Width}x{Height}",
                        currentWidth,
                        currentHeight
                    );
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        ex,
                        "Failed to create render target: {Width}x{Height}",
                        currentWidth,
                        currentHeight
                    );
                    return null;
                }
            }

            return _sceneRenderTarget;
        }

        /// <summary>
        /// Disposes the current render target.
        /// </summary>
        public void DisposeRenderTarget()
        {
            if (_sceneRenderTarget != null)
            {
                _sceneRenderTarget.Dispose();
                _sceneRenderTarget = null;
                _logger.Debug("Disposed render target");
            }
        }

        /// <summary>
        /// Disposes all resources.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                DisposeRenderTarget();
                _disposed = true;
            }
        }
    }
}
