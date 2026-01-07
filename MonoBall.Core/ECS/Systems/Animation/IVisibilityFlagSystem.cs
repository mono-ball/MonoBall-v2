namespace MonoBall.Core.ECS.Systems.Animation;

/// <summary>
///     Interface for visibility flag system.
///     Updates entity visibility based on flag values.
/// </summary>
/// <remarks>
///     VisibilityFlagSystem is event/update-driven with no public methods.
///     This interface exists for dependency inversion in IAnimationSystems.
/// </remarks>
public interface IVisibilityFlagSystem
{
    // System is event/update-driven with no public API
    // Interface exists for dependency inversion in IAnimationSystems
}
