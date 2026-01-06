namespace MonoBall.Core.ECS.Systems.Animation;

/// <summary>
///     Interface for sprite animation system.
///     Updates animation timers and advances frames for NPCs and Players.
/// </summary>
/// <remarks>
///     SpriteAnimationSystem is update-driven with no public methods.
///     This interface exists for dependency inversion in IAnimationSystems.
/// </remarks>
public interface ISpriteAnimationSystem
{
    // System is update-driven with no public API
    // Interface exists for dependency inversion in IAnimationSystems
}
