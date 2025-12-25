using Arch.Core;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scenes
{
    /// <summary>
    /// Interface for scene-specific systems that handle update and rendering for a particular scene type.
    /// Provides abstraction for scene systems without exposing concrete implementations.
    /// </summary>
    public interface ISceneSystem
    {
        /// <summary>
        /// Updates a specific scene entity.
        /// Called by SceneSystem when iterating through scenes.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to update.</param>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        void Update(Entity sceneEntity, float deltaTime);

        /// <summary>
        /// Renders a specific scene entity.
        /// Called by SceneSystem when rendering scenes.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to render.</param>
        /// <param name="gameTime">The game time.</param>
        void RenderScene(Entity sceneEntity, GameTime gameTime);

        /// <summary>
        /// Performs internal processing that needs to run every frame.
        /// This is for systems that need to process queues, update animations, etc.
        /// Called by SceneSystem after per-scene updates.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        /// <remarks>
        /// Most scene systems don't need internal processing and can leave this empty.
        /// Systems like LoadingSceneSystem (processes progress queue) and MapPopupSceneSystem
        /// (updates popup animations) override this to handle their internal state.
        /// </remarks>
        void ProcessInternal(float deltaTime);
    }
}
