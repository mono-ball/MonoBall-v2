using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using Serilog;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Manages RenderTarget2D lifecycle for post-processing effects.
///     Handles creation, resizing, and disposal of render targets.
///     Supports multiple render targets with depth buffers.
/// </summary>
public class RenderTargetManager : IDisposable
{
    private readonly Dictionary<int, DepthFormat> _depthFormats = new();
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ILogger _logger;

    // Multiple render targets support
    private readonly Dictionary<int, RenderTarget2D> _renderTargets = new();
    private readonly Dictionary<int, (int width, int height)> _renderTargetSizes = new();
    private readonly Dictionary<int, SurfaceFormat> _surfaceFormats = new();
    private bool _disposed;
    private int _lastHeight;
    private int _lastWidth;
    private RenderTarget2D? _sceneRenderTarget;

    /// <summary>
    ///     Initializes a new instance of the RenderTargetManager.
    /// </summary>
    /// <param name="graphicsDevice">The graphics device for creating render targets.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public RenderTargetManager(GraphicsDevice graphicsDevice, ILogger logger)
    {
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lastWidth = graphicsDevice.Viewport.Width;
        _lastHeight = graphicsDevice.Viewport.Height;
    }

    /// <summary>
    ///     Disposes all resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            DisposeAllRenderTargets();
            _disposed = true;
        }
    }

    /// <summary>
    ///     Gets or creates a render target matching the current viewport dimensions.
    ///     Automatically recreates the render target if viewport size changes.
    /// </summary>
    /// <returns>The render target, or null if creation fails.</returns>
    public RenderTarget2D? GetOrCreateRenderTarget()
    {
        return GetOrCreateRenderTarget(0);
    }

    /// <summary>
    ///     Gets or creates a render target by index with optional depth buffer.
    ///     Automatically recreates the render target if viewport size or format changes.
    /// </summary>
    /// <param name="index">The render target index (0 = default scene render target).</param>
    /// <param name="depthFormat">The depth format (default: None).</param>
    /// <param name="surfaceFormat">The surface format (default: Color).</param>
    /// <returns>The render target, or null if creation fails.</returns>
    public RenderTarget2D? GetOrCreateRenderTarget(
        int index,
        DepthFormat depthFormat = DepthFormat.None,
        SurfaceFormat surfaceFormat = SurfaceFormat.Color
    )
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RenderTargetManager));

        var currentWidth = _graphicsDevice.Viewport.Width;
        var currentHeight = _graphicsDevice.Viewport.Height;

        // Check if render target exists and matches current requirements
        if (_renderTargets.TryGetValue(index, out var existingTarget))
        {
            var existingSize = _renderTargetSizes[index];
            var existingDepth = _depthFormats.GetValueOrDefault(index, DepthFormat.None);
            var existingSurface = _surfaceFormats.GetValueOrDefault(index, SurfaceFormat.Color);

            if (
                existingSize.width == currentWidth
                && existingSize.height == currentHeight
                && existingDepth == depthFormat
                && existingSurface == surfaceFormat
            )
                // Render target is valid, return it
                return existingTarget;

            // Size or format changed - recreate
            DisposeRenderTarget(index);
        }

        // Create new render target
        try
        {
            var newTarget = new RenderTarget2D(
                _graphicsDevice,
                currentWidth,
                currentHeight,
                false,
                surfaceFormat,
                depthFormat
            );

            _renderTargets[index] = newTarget;
            _depthFormats[index] = depthFormat;
            _surfaceFormats[index] = surfaceFormat;
            _renderTargetSizes[index] = (currentWidth, currentHeight);

            // Also update legacy _sceneRenderTarget for backward compatibility
            if (index == 0)
            {
                _sceneRenderTarget = newTarget;
                _lastWidth = currentWidth;
                _lastHeight = currentHeight;
            }

            _logger.Debug(
                "Created render target {Index}: {Width}x{Height}, Surface: {SurfaceFormat}, Depth: {DepthFormat}",
                index,
                currentWidth,
                currentHeight,
                surfaceFormat,
                depthFormat
            );

            return newTarget;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to create render target {Index}: {Width}x{Height}, Surface: {SurfaceFormat}, Depth: {DepthFormat}",
                index,
                currentWidth,
                currentHeight,
                surfaceFormat,
                depthFormat
            );
            return null;
        }
    }

    /// <summary>
    ///     Gets or creates a render target with explicit dimensions and depth format.
    /// </summary>
    /// <param name="index">The render target index.</param>
    /// <param name="width">The render target width.</param>
    /// <param name="height">The render target height.</param>
    /// <param name="depthFormat">The depth format (default: None).</param>
    /// <param name="surfaceFormat">The surface format (default: Color).</param>
    /// <returns>The render target, or null if creation fails.</returns>
    public RenderTarget2D? GetOrCreateRenderTarget(
        int index,
        int width,
        int height,
        DepthFormat depthFormat = DepthFormat.None,
        SurfaceFormat surfaceFormat = SurfaceFormat.Color
    )
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RenderTargetManager));

        // Check if render target exists and matches requirements
        if (_renderTargets.TryGetValue(index, out var existingTarget))
        {
            var existingSize = _renderTargetSizes[index];
            var existingDepth = _depthFormats.GetValueOrDefault(index, DepthFormat.None);
            var existingSurface = _surfaceFormats.GetValueOrDefault(index, SurfaceFormat.Color);

            if (
                existingSize.width == width
                && existingSize.height == height
                && existingDepth == depthFormat
                && existingSurface == surfaceFormat
            )
                // Render target is valid, return it
                return existingTarget;

            // Size or format changed - recreate
            DisposeRenderTarget(index);
        }

        // Create new render target
        try
        {
            var newTarget = new RenderTarget2D(
                _graphicsDevice,
                width,
                height,
                false,
                surfaceFormat,
                depthFormat
            );

            _renderTargets[index] = newTarget;
            _depthFormats[index] = depthFormat;
            _surfaceFormats[index] = surfaceFormat;
            _renderTargetSizes[index] = (width, height);

            // Also update legacy _sceneRenderTarget for backward compatibility
            if (index == 0)
            {
                _sceneRenderTarget = newTarget;
                _lastWidth = width;
                _lastHeight = height;
            }

            _logger.Debug(
                "Created render target {Index}: {Width}x{Height}, Surface: {SurfaceFormat}, Depth: {DepthFormat}",
                index,
                width,
                height,
                surfaceFormat,
                depthFormat
            );

            return newTarget;
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to create render target {Index}: {Width}x{Height}, Surface: {SurfaceFormat}, Depth: {DepthFormat}",
                index,
                width,
                height,
                surfaceFormat,
                depthFormat
            );
            return null;
        }
    }

    /// <summary>
    ///     Disposes a specific render target by index.
    /// </summary>
    /// <param name="index">The render target index.</param>
    public void DisposeRenderTarget(int index)
    {
        if (_renderTargets.TryGetValue(index, out var target))
        {
            target.Dispose();
            _renderTargets.Remove(index);
            _depthFormats.Remove(index);
            _surfaceFormats.Remove(index);
            _renderTargetSizes.Remove(index);

            // Also clear legacy _sceneRenderTarget if index 0
            if (index == 0)
                _sceneRenderTarget = null;

            _logger.Debug("Disposed render target {Index}", index);
        }
    }

    /// <summary>
    ///     Disposes the default render target (index 0).
    /// </summary>
    public void DisposeRenderTarget()
    {
        DisposeRenderTarget(0);
    }

    /// <summary>
    ///     Disposes all render targets.
    /// </summary>
    public void DisposeAllRenderTargets()
    {
        var indices = new List<int>(_renderTargets.Keys);
        foreach (var index in indices)
            DisposeRenderTarget(index);
    }

    /// <summary>
    ///     Clears the render target pool (for testing/reset).
    /// </summary>
    public void ClearRenderTargetPool()
    {
        DisposeAllRenderTargets();
    }
}
