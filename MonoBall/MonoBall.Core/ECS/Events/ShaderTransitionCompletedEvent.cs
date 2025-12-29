using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a shader transition (crossfade) completes.
///     Subscribe via EventBus.Subscribe&lt;ShaderTransitionCompletedEvent&gt;(handler).
/// </summary>
public struct ShaderTransitionCompletedEvent
{
    /// <summary>
    ///     The entity whose transition completed.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     The shader ID that was transitioned from (null if no previous shader).
    /// </summary>
    public string? FromShaderId { get; set; }

    /// <summary>
    ///     The shader ID that was transitioned to.
    /// </summary>
    public string ToShaderId { get; set; }

    /// <summary>
    ///     The layer the transition occurred on.
    /// </summary>
    public ShaderLayer Layer { get; set; }
}
