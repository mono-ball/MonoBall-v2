using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Coordinates rendering state for scene rendering.
///     Manages viewport, render targets, and SpriteBatch lifecycle.
/// </summary>
public interface ISceneRenderingCoordinator
{
    /// <summary>
    ///     Prepares rendering state for a scene.
    ///     Sets viewport, render target, and begins SpriteBatch with correct transform.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to prepare.</param>
    /// <param name="camera">The camera component for this scene (null for ScreenCamera).</param>
    /// <returns>Render context with prepared state.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when scene entity does not have SceneComponent.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when sceneEntity is invalid or required dependencies are null.
    /// </exception>
    IRenderContext PrepareScene(Entity sceneEntity, CameraComponent? camera);

    /// <summary>
    ///     Finishes rendering for a scene.
    ///     Ends SpriteBatch, restores viewport/render target, applies post-processing.
    /// </summary>
    /// <param name="context">The render context from PrepareScene.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when context is null.
    /// </exception>
    void FinishScene(IRenderContext context);
}
