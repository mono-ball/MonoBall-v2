using System;
using Microsoft.Xna.Framework.Graphics;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Interface for render target lifecycle management.
///     Handles creation, resizing, and disposal of render targets.
/// </summary>
public interface IRenderTargetManager : IDisposable
{
    /// <summary>
    ///     Gets or creates a render target matching the current viewport dimensions.
    ///     Automatically recreates the render target if viewport size changes.
    /// </summary>
    /// <returns>The render target, or null if creation fails.</returns>
    RenderTarget2D? GetOrCreateRenderTarget();

    /// <summary>
    ///     Gets or creates a render target by index with optional depth buffer.
    ///     Automatically recreates the render target if viewport size or format changes.
    /// </summary>
    /// <param name="index">The render target index (0 = default scene render target).</param>
    /// <param name="depthFormat">The depth format (default: None).</param>
    /// <param name="surfaceFormat">The surface format (default: Color).</param>
    /// <returns>The render target, or null if creation fails.</returns>
    RenderTarget2D? GetOrCreateRenderTarget(
        int index,
        DepthFormat depthFormat = DepthFormat.None,
        SurfaceFormat surfaceFormat = SurfaceFormat.Color
    );

    /// <summary>
    ///     Gets or creates a render target with explicit dimensions and depth format.
    /// </summary>
    /// <param name="index">The render target index.</param>
    /// <param name="width">The render target width.</param>
    /// <param name="height">The render target height.</param>
    /// <param name="depthFormat">The depth format (default: None).</param>
    /// <param name="surfaceFormat">The surface format (default: Color).</param>
    /// <returns>The render target, or null if creation fails.</returns>
    RenderTarget2D? GetOrCreateRenderTarget(
        int index,
        int width,
        int height,
        DepthFormat depthFormat = DepthFormat.None,
        SurfaceFormat surfaceFormat = SurfaceFormat.Color
    );

    /// <summary>
    ///     Disposes a specific render target by index.
    /// </summary>
    /// <param name="index">The render target index.</param>
    void DisposeRenderTarget(int index);

    /// <summary>
    ///     Disposes the default render target (index 0).
    /// </summary>
    void DisposeRenderTarget();

    /// <summary>
    ///     Disposes all render targets.
    /// </summary>
    void DisposeAllRenderTargets();

    /// <summary>
    ///     Clears the render target pool (for testing/reset).
    /// </summary>
    void ClearRenderTargetPool();
}
