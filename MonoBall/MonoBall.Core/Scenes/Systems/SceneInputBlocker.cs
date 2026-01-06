using System;
using MonoBall.Core.ECS.Services;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     Input blocker that checks if any active scene has BlocksInput=true.
///     Used by InputSystem to prevent player movement when message boxes or other blocking scenes are active.
/// </summary>
public class SceneInputBlocker : IInputBlocker
{
    private readonly Func<ISceneSystems?> _getSceneSystems;

    /// <summary>
    ///     Initializes a new instance of the SceneInputBlocker.
    /// </summary>
    /// <param name="getSceneSystems">Function that returns the scene systems bundle (may be null if not yet initialized).</param>
    public SceneInputBlocker(Func<ISceneSystems?> getSceneSystems)
    {
        _getSceneSystems =
            getSceneSystems ?? throw new ArgumentNullException(nameof(getSceneSystems));
    }

    /// <summary>
    ///     Gets whether input is currently blocked by any active scene.
    /// </summary>
    public bool IsInputBlocked
    {
        get
        {
            var sceneSystems = _getSceneSystems();
            if (sceneSystems == null || !sceneSystems.IsAvailable)
                // Scene systems not yet initialized, don't block input
                return false;

            var isBlocked = false;
            sceneSystems.IterateScenes(
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
