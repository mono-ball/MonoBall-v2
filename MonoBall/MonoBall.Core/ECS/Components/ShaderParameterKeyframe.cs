namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Represents a single keyframe in a shader parameter animation timeline.
///     Keyframes define values at specific points in time for interpolation.
/// </summary>
public struct ShaderParameterKeyframe
{
    /// <summary>
    ///     Gets or sets the time at which this keyframe occurs (in seconds).
    /// </summary>
    public float Time { get; set; }

    /// <summary>
    ///     Gets or sets the parameter value at this keyframe.
    ///     Supported types: float, Vector2, Vector3, Vector4, Color.
    /// </summary>
    public object Value { get; set; }

    /// <summary>
    ///     Gets or sets the easing function to use when interpolating from this keyframe to the next.
    /// </summary>
    public EasingFunction Easing { get; set; }
}
