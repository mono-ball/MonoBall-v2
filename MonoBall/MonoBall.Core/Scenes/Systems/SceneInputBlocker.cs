using System;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Scenes.Components;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// Input blocker that checks if any active scene has BlocksInput=true.
    /// Used by InputSystem to prevent player movement when message boxes or other blocking scenes are active.
    /// </summary>
    public class SceneInputBlocker : IInputBlocker
    {
        private readonly Func<SceneSystem?> _getSceneSystem;

        /// <summary>
        /// Initializes a new instance of the SceneInputBlocker.
        /// </summary>
        /// <param name="getSceneSystem">Function that returns the scene system (may be null if not yet initialized).</param>
        public SceneInputBlocker(Func<SceneSystem?> getSceneSystem)
        {
            _getSceneSystem =
                getSceneSystem ?? throw new ArgumentNullException(nameof(getSceneSystem));
        }

        /// <summary>
        /// Gets whether input is currently blocked by any active scene.
        /// </summary>
        public bool IsInputBlocked
        {
            get
            {
                var sceneSystem = _getSceneSystem();
                if (sceneSystem == null)
                {
                    // Scene system not yet initialized, don't block input
                    return false;
                }

                bool isBlocked = false;
                sceneSystem.IterateScenes(
                    (sceneEntity, sceneComponent) =>
                    {
                        // Check if this scene blocks input
                        if (
                            sceneComponent.IsActive
                            && !sceneComponent.IsPaused
                            && sceneComponent.BlocksInput
                        )
                        {
                            isBlocked = true;
                            return false; // Stop iterating
                        }
                        return true; // Continue iterating
                    }
                );
                return isBlocked;
            }
        }
    }
}
