namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for shader parameter animation system.
///     Animates shader parameters over time using easing functions.
/// </summary>
/// <remarks>
///     ShaderParameterAnimationSystem is primarily update-driven with no public methods.
///     This interface exists for dependency inversion in IShaderSystems.
/// </remarks>
public interface IShaderParameterAnimationSystem
{
    // System is update-driven with no public API
    // Interface exists for dependency inversion in IShaderSystems
}
