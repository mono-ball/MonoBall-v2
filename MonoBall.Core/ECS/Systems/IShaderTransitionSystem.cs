using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for shader transition operations.
///     Handles crossfade blending between shaders.
/// </summary>
public interface IShaderTransitionSystem
{
    /// <summary>
    ///     Starts a transition on an entity.
    /// </summary>
    /// <param name="entity">The entity with RenderingShaderComponent.</param>
    /// <param name="fromShaderId">The source shader ID (can be null).</param>
    /// <param name="toShaderId">The target shader ID.</param>
    /// <param name="duration">Transition duration in seconds.</param>
    /// <param name="easing">The easing function.</param>
    void StartTransition(
        Entity entity,
        string? fromShaderId,
        string toShaderId,
        float duration,
        EasingFunction easing = EasingFunction.Linear
    );

    /// <summary>
    ///     Cancels any active transition on an entity.
    /// </summary>
    /// <param name="entity">The entity to cancel transition on.</param>
    void CancelTransition(Entity entity);

    /// <summary>
    ///     Gets the current blend weight for an entity's transition.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>Blend weight (0.0-1.0), or 0 if no transition active.</returns>
    float GetBlendWeight(Entity entity);

    /// <summary>
    ///     Checks if an entity has an active transition.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if transition is in progress.</returns>
    bool IsTransitioning(Entity entity);
}
