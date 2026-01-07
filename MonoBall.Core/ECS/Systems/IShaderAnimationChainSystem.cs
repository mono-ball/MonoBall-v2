using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Utilities;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for shader animation chain operations.
///     Executes sequenced shader animation phases.
/// </summary>
public interface IShaderAnimationChainSystem
{
    /// <summary>
    ///     Sets the animation chain for an entity.
    /// </summary>
    /// <param name="entity">The entity to set chain for.</param>
    /// <param name="phases">The phase data (delays and durations).</param>
    /// <param name="animations">The animations for each phase (keyed by phase index).</param>
    void SetChain(
        Entity entity,
        List<ShaderAnimationPhaseData> phases,
        Dictionary<int, List<ShaderAnimationData>> animations
    );

    /// <summary>
    ///     Clears the animation chain for an entity.
    /// </summary>
    /// <param name="entity">The entity to clear chain for.</param>
    void ClearChain(Entity entity);

    /// <summary>
    ///     Stops an animation chain on an entity.
    /// </summary>
    /// <param name="entity">The entity to stop.</param>
    void StopChain(Entity entity);

    /// <summary>
    ///     Pauses an animation chain on an entity.
    /// </summary>
    /// <param name="entity">The entity to pause.</param>
    void PauseChain(Entity entity);

    /// <summary>
    ///     Resumes a paused animation chain on an entity.
    /// </summary>
    /// <param name="entity">The entity to resume.</param>
    void ResumeChain(Entity entity);
}
