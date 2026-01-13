using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Render context provided by SceneRenderingCoordinator.
///     Contains prepared rendering state for a scene.
/// </summary>
public interface IRenderContext
{
    /// <summary>
    ///     The scene entity being rendered.
    /// </summary>
    Entity SceneEntity { get; }

    /// <summary>
    ///     The camera component for this scene (null for ScreenCamera).
    /// </summary>
    CameraComponent? Camera { get; }

    /// <summary>
    ///     The SpriteBatch (already begun, ready for drawing).
    /// </summary>
    SpriteBatch SpriteBatch { get; }

    /// <summary>
    ///     Gets the viewport scale factor based on viewport dimensions relative to reference width.
    ///     Calculates scale from camera viewport, not camera zoom.
    /// </summary>
    /// <param name="referenceWidth">The reference width (e.g., GBA reference resolution width).</param>
    /// <returns>The scale factor (1.0 = no scaling, >1.0 = upscaled).</returns>
    float GetViewportScale(int referenceWidth);

    /// <summary>
    ///     Marks that the SpriteBatch was ended by a system (e.g., for shader changes).
    ///     Coordinator will check this before attempting to end the batch.
    /// </summary>
    void MarkBatchEnded();

    /// <summary>
    ///     Marks that a new batch was started after ending the coordinator's batch.
    ///     Coordinator will end this new batch in FinishScene() unless it was already ended.
    /// </summary>
    void MarkNewBatchStarted();

    /// <summary>
    ///     Marks that the new batch (started after ending coordinator's batch) was ended by the system.
    ///     Coordinator will skip ending this batch in FinishScene().
    /// </summary>
    void MarkNewBatchEnded();

    /// <summary>
    ///     Gets whether the SpriteBatch has been ended by a system.
    /// </summary>
    bool IsBatchEnded { get; }

    /// <summary>
    ///     Gets whether a new batch was started after ending the coordinator's batch.
    /// </summary>
    bool HasNewBatchStarted { get; }

    /// <summary>
    ///     Gets whether the new batch (started after ending coordinator's batch) was ended by the system.
    /// </summary>
    bool IsNewBatchEnded { get; }
}
