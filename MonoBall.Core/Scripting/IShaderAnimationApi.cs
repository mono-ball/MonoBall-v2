using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Scripting;

/// <summary>
///     Animation, transition, and preset control API.
///     Provides methods to animate parameters, transition between shaders, and apply presets.
///     Split from IShaderApi per Interface Segregation Principle (ISP).
/// </summary>
public interface IShaderAnimationApi
{
    /// <summary>
    ///     Animates a shader parameter on an entity.
    ///     Creates a ShaderParameterAnimationComponent.
    /// </summary>
    /// <param name="entity">The entity with a shader component.</param>
    /// <param name="paramName">The parameter name to animate.</param>
    /// <param name="from">The starting value.</param>
    /// <param name="to">The ending value.</param>
    /// <param name="duration">Animation duration in seconds.</param>
    /// <param name="easing">The easing function to use. Default is Linear.</param>
    /// <param name="isLooping">Whether to loop the animation. Default is false.</param>
    /// <param name="pingPong">Whether to ping-pong (requires looping). Default is false.</param>
    /// <exception cref="System.ArgumentException">Thrown if entity is not alive.</exception>
    /// <exception cref="System.ArgumentNullException">Thrown if paramName is null.</exception>
    /// <exception cref="System.InvalidOperationException">Thrown if entity has no shader component.</exception>
    void AnimateParameter(
        Entity entity,
        string paramName,
        object from,
        object to,
        float duration,
        EasingFunction easing = EasingFunction.Linear,
        bool isLooping = false,
        bool pingPong = false
    );

    /// <summary>
    ///     Stops a parameter animation on an entity.
    /// </summary>
    /// <param name="entity">The entity with the animation.</param>
    /// <param name="paramName">The parameter name to stop animating.</param>
    /// <returns>True if animation was found and stopped, false otherwise.</returns>
    bool StopAnimation(Entity entity, string paramName);

    /// <summary>
    ///     Transitions from one layer shader to another with crossfade blending.
    ///     Uses dual-render blend (render both shaders, interpolate outputs).
    /// </summary>
    /// <param name="layer">The layer to transition on.</param>
    /// <param name="fromShaderId">The current shader ID (or null for no shader).</param>
    /// <param name="toShaderId">The target shader ID.</param>
    /// <param name="duration">Transition duration in seconds.</param>
    /// <param name="easing">The easing function. Default is Linear.</param>
    /// <returns>The shader entity being transitioned, or null if not found.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown if toShaderId is null.</exception>
    Entity? TransitionToShader(
        ShaderLayer layer,
        string? fromShaderId,
        string toShaderId,
        float duration,
        EasingFunction easing = EasingFunction.Linear
    );

    /// <summary>
    ///     Applies a shader preset to an entity with optional transition.
    ///     Uses IShaderPresetService to resolve parameters.
    /// </summary>
    /// <param name="entity">The entity to apply preset to.</param>
    /// <param name="presetId">The preset ID.</param>
    /// <param name="transitionDuration">Duration to transition parameters (0 for immediate).</param>
    /// <exception cref="System.ArgumentException">Thrown if entity is not alive.</exception>
    /// <exception cref="System.ArgumentNullException">Thrown if presetId is null.</exception>
    void ApplyPreset(Entity entity, string presetId, float transitionDuration = 0);

    /// <summary>
    ///     Creates a fluent animation chain builder for an entity.
    ///     Allows sequencing multiple animation phases.
    /// </summary>
    /// <param name="entity">The entity to build animations for.</param>
    /// <returns>A new ShaderAnimationBuilder for fluent configuration.</returns>
    /// <exception cref="System.ArgumentException">Thrown if entity is not alive.</exception>
    ShaderAnimationBuilder CreateAnimationChain(Entity entity);
}
