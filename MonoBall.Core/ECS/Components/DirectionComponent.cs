namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component wrapper for Direction enum to enable ECS queries.
///     Stores the current facing direction of an entity.
/// </summary>
public struct DirectionComponent
{
    /// <summary>
    ///     Gets or sets the direction value.
    /// </summary>
    public Direction Value { get; set; }
}
