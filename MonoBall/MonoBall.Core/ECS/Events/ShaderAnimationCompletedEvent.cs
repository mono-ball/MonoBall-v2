using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a shader parameter animation completes (non-looping animations only).
///     Subscribe via EventBus.Subscribe&lt;ShaderAnimationCompletedEvent&gt;(handler).
/// </summary>
public struct ShaderAnimationCompletedEvent
{
    /// <summary>
    ///     The entity whose animation completed.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     The parameter name that was being animated.
    /// </summary>
    public string ParameterName { get; set; }

    /// <summary>
    ///     The shader ID that was being animated.
    /// </summary>
    public string ShaderId { get; set; }

    /// <summary>
    ///     The layer the shader was on (SpriteLayer for entity shaders).
    /// </summary>
    public ShaderLayer Layer { get; set; }

    /// <summary>
    ///     The final value of the parameter after animation.
    /// </summary>
    public object? FinalValue { get; set; }
}
