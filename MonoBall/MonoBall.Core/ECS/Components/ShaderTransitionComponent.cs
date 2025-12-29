namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component for transitioning between shaders with crossfade blending.
///     Uses dual-render blend: render both shaders to separate targets, then blend outputs.
/// </summary>
public struct ShaderTransitionComponent
{
    /// <summary>
    ///     The shader ID being transitioned from (null for no previous shader).
    /// </summary>
    public string? FromShaderId { get; set; }

    /// <summary>
    ///     The shader ID being transitioned to.
    /// </summary>
    public string? ToShaderId { get; set; }

    /// <summary>
    ///     Total duration of the transition in seconds.
    /// </summary>
    public float Duration { get; set; }

    /// <summary>
    ///     Elapsed time since transition started (in seconds).
    /// </summary>
    public float ElapsedTime { get; set; }

    /// <summary>
    ///     The easing function for the transition.
    /// </summary>
    public EasingFunction Easing { get; set; }

    /// <summary>
    ///     Current blend weight (0.0 = From, 1.0 = To).
    /// </summary>
    public float BlendWeight { get; set; }

    /// <summary>
    ///     Current state of the transition.
    /// </summary>
    public ShaderTransitionState State { get; set; }

    /// <summary>
    ///     Creates a new transition component.
    /// </summary>
    public static ShaderTransitionComponent Create(
        string? fromShaderId,
        string toShaderId,
        float duration,
        EasingFunction easing = EasingFunction.Linear
    )
    {
        return new ShaderTransitionComponent
        {
            FromShaderId = fromShaderId,
            ToShaderId = toShaderId,
            Duration = duration,
            ElapsedTime = 0f,
            Easing = easing,
            BlendWeight = 0f,
            State = ShaderTransitionState.NotStarted,
        };
    }
}

/// <summary>
///     States for shader transition.
/// </summary>
public enum ShaderTransitionState
{
    /// <summary>
    ///     Transition has not yet started.
    /// </summary>
    NotStarted,

    /// <summary>
    ///     Transition is in progress.
    /// </summary>
    InProgress,

    /// <summary>
    ///     Transition completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    ///     Transition was cancelled.
    /// </summary>
    Cancelled,
}
