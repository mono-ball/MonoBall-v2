using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a phase in a shader animation chain completes.
///     Subscribe via EventBus.Subscribe&lt;ShaderAnimationPhaseCompletedEvent&gt;(handler).
/// </summary>
public struct ShaderAnimationPhaseCompletedEvent
{
    /// <summary>
    ///     The entity whose animation phase completed.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     The shader ID being animated.
    /// </summary>
    public string ShaderId { get; set; }

    /// <summary>
    ///     The index of the phase that completed (0-based).
    /// </summary>
    public int PhaseIndex { get; set; }

    /// <summary>
    ///     The total number of phases in the chain.
    /// </summary>
    public int TotalPhases { get; set; }

    /// <summary>
    ///     Whether there are more phases remaining.
    /// </summary>
    public bool HasMorePhases { get; set; }
}
