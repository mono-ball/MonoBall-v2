namespace MonoBall.Core.ECS.Systems.Animation;

/// <summary>
///     Interface for sprite sheet system.
///     Handles sprite sheet change requests for entities with multiple sprite sheets.
/// </summary>
/// <remarks>
///     SpriteSheetSystem is event-driven with no public methods.
///     This interface exists for dependency inversion in IAnimationSystems.
/// </remarks>
public interface ISpriteSheetSystem
{
    // System is event-driven with no public API
    // Interface exists for dependency inversion in IAnimationSystems
}
