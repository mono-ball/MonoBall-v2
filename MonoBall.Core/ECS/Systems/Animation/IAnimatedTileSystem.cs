namespace MonoBall.Core.ECS.Systems.Animation;

/// <summary>
///     Interface for animated tile system.
///     Updates animation timers and advances frames for animated tiles.
/// </summary>
/// <remarks>
///     AnimatedTileSystem is update-driven with no public methods.
///     This interface exists for dependency inversion in IAnimationSystems.
/// </remarks>
public interface IAnimatedTileSystem
{
    // System is update-driven with no public API
    // Interface exists for dependency inversion in IAnimationSystems
}
