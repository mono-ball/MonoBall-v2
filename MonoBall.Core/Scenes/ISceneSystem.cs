using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.Scenes.Systems;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Interface for scene-specific systems that handle update and rendering for a particular scene type.
///     Provides abstraction for scene systems without exposing concrete implementations.
/// </summary>
public interface ISceneSystem
{
    /// <summary>
    ///     Updates a specific scene entity.
    ///     Called by SceneSystem when iterating through scenes.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to update.</param>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    void Update(Entity sceneEntity, float deltaTime);

    /// <summary>
    ///     Renders a specific scene entity.
    ///     Called by SceneSystem when rendering scenes.
    ///     The render context contains a prepared SpriteBatch (already begun) and camera information.
    /// </summary>
    /// <param name="sceneEntity">The scene entity to render.</param>
    /// <param name="gameTime">The game time.</param>
    /// <param name="renderContext">The render context with prepared SpriteBatch and camera. Required - coordinator always provides this.</param>
    /// <exception cref="ArgumentNullException">Thrown when renderContext is null.</exception>
    void RenderScene(Entity sceneEntity, GameTime gameTime, IRenderContext renderContext);

    /// <summary>
    ///     Performs internal processing that needs to run every frame.
    ///     This is for systems that need to process queues, update animations, etc.
    ///     Called by SceneSystem after per-scene updates.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    /// <remarks>
    ///     Most scene systems don't need internal processing and can leave this empty.
    ///     Systems like LoadingSceneSystem (processes progress queue) and MapPopupSceneSystem
    ///     (updates popup animations) override this to handle their internal state.
    /// </remarks>
    void ProcessInternal(float deltaTime);
}
