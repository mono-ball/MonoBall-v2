using Arch.Core;
using MonoBall.Core.Scenes.Components;

namespace MonoBall.Core.Scenes
{
    /// <summary>
    /// Interface for managing scene lifecycle (creation and destruction).
    /// Provides abstraction for scene management operations without exposing full SceneSystem implementation.
    /// </summary>
    public interface ISceneManager
    {
        /// <summary>
        /// Creates a scene entity and adds it to the scene stack.
        /// </summary>
        /// <param name="sceneComponent">The scene component data.</param>
        /// <param name="additionalComponents">Additional components to add to the scene entity.</param>
        /// <returns>The created scene entity.</returns>
        Entity CreateScene(SceneComponent sceneComponent, params object[] additionalComponents);

        /// <summary>
        /// Destroys a scene entity and removes it from the scene stack.
        /// </summary>
        /// <param name="sceneEntity">The scene entity to destroy.</param>
        void DestroyScene(Entity sceneEntity);

        /// <summary>
        /// Destroys a scene entity by scene ID.
        /// </summary>
        /// <param name="sceneId">The scene ID.</param>
        void DestroyScene(string sceneId);
    }
}
