using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Internal interface for render context state used by SceneRenderingCoordinator.
///     Provides access to internal state without requiring downcasting.
/// </summary>
internal interface IRenderContextInternal
{
    /// <summary>
    ///     Gets the saved viewport that was active before scene rendering.
    /// </summary>
    Viewport SavedViewport { get; }

    /// <summary>
    ///     Gets the saved render target that was active before scene rendering.
    /// </summary>
    RenderTarget2D? SavedRenderTarget { get; }

    /// <summary>
    ///     Gets the render target used for post-processing (if any).
    /// </summary>
    RenderTarget2D? RenderTarget { get; }

    /// <summary>
    ///     Gets whether this scene has post-processing shaders.
    /// </summary>
    bool HasPostProcessing { get; }

    /// <summary>
    ///     Gets the shader stack for post-processing (if any).
    /// </summary>
    IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)>? ShaderStack { get; }
}

/// <summary>
///     Internal render context implementation.
///     Class to allow batch state tracking (required for coordinator cleanup).
///     Uses class instead of struct to ensure state mutations propagate correctly.
/// </summary>
internal class RenderContext : IRenderContext, IRenderContextInternal
{
    /// <summary>
    ///     The scene entity being rendered.
    /// </summary>
    public Entity SceneEntity { get; init; }

    /// <summary>
    ///     The camera component for this scene (null for ScreenCamera).
    /// </summary>
    public CameraComponent? Camera { get; init; }

    /// <summary>
    ///     The SpriteBatch (already begun, ready for drawing).
    /// </summary>
    public SpriteBatch SpriteBatch { get; init; }

    /// <summary>
    ///     Gets whether the SpriteBatch has been ended by a system.
    ///     Can be set during initialization or via MarkBatchEnded().
    /// </summary>
    public bool IsBatchEnded { get; set; }

    /// <summary>
    ///     Gets whether a new batch was started after ending the coordinator's batch.
    /// </summary>
    public bool HasNewBatchStarted { get; set; }

    /// <summary>
    ///     Gets whether the new batch (started after ending coordinator's batch) was ended by the system.
    /// </summary>
    public bool IsNewBatchEnded { get; set; }

    /// <summary>
    ///     Gets the viewport scale factor based on viewport dimensions relative to reference width.
    ///     Calculates scale from camera viewport, not camera zoom.
    /// </summary>
    /// <param name="referenceWidth">The reference width (e.g., GBA reference resolution width).</param>
    /// <returns>The scale factor (1.0 = no scaling, >1.0 = upscaled).</returns>
    public float GetViewportScale(int referenceWidth)
    {
        if (!Camera.HasValue)
            return 1.0f;

        var camera = Camera.Value;
        var viewportWidth = camera.VirtualViewport != Rectangle.Empty
            ? camera.VirtualViewport.Width
            : camera.Viewport.Width;
        return (float)viewportWidth / referenceWidth;
    }

    /// <summary>
    ///     Marks that the SpriteBatch was ended by a system (e.g., for shader changes).
    ///     Coordinator will check this before attempting to end the batch.
    /// </summary>
    public void MarkBatchEnded()
    {
        IsBatchEnded = true;
    }

    /// <summary>
    ///     Marks that a new batch was started after ending the coordinator's batch.
    ///     Coordinator will end this new batch in FinishScene() unless it was already ended.
    /// </summary>
    public void MarkNewBatchStarted()
    {
        HasNewBatchStarted = true;
        IsNewBatchEnded = false; // Reset when starting new batch
    }

    /// <summary>
    ///     Marks that the new batch (started after ending coordinator's batch) was ended by the system.
    ///     Coordinator will skip ending this batch in FinishScene().
    /// </summary>
    public void MarkNewBatchEnded()
    {
        IsNewBatchEnded = true;
    }

    // Internal state for coordinator (via IRenderContextInternal interface)
    /// <summary>
    ///     Gets the saved viewport that was active before scene rendering.
    /// </summary>
    Viewport IRenderContextInternal.SavedViewport => SavedViewport;

    /// <summary>
    ///     Gets the saved render target that was active before scene rendering.
    /// </summary>
    RenderTarget2D? IRenderContextInternal.SavedRenderTarget => SavedRenderTarget;

    /// <summary>
    ///     Gets the render target used for post-processing (if any).
    /// </summary>
    RenderTarget2D? IRenderContextInternal.RenderTarget => RenderTarget;

    /// <summary>
    ///     Gets whether this scene has post-processing shaders.
    /// </summary>
    bool IRenderContextInternal.HasPostProcessing => HasPostProcessing;

    /// <summary>
    ///     Gets the shader stack for post-processing (if any).
    /// </summary>
    IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)>? IRenderContextInternal.ShaderStack => ShaderStack;

    // Internal state properties (used by struct initialization)
    internal Viewport SavedViewport { get; init; }
    internal RenderTarget2D? SavedRenderTarget { get; init; }
    internal RenderTarget2D? RenderTarget { get; init; }
    internal bool HasPostProcessing { get; init; }
    internal IReadOnlyList<(Effect effect, ShaderBlendMode blendMode, Entity entity)>? ShaderStack { get; init; }
}
