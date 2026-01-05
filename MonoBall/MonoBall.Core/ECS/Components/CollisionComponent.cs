namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that defines collision behavior for an entity.
///     Used by CollisionService to determine if an entity blocks movement.
/// </summary>
public struct CollisionComponent
{
    /// <summary>
    ///     Whether this entity blocks movement of other entities.
    ///     When true, other entities cannot move into this entity's tile.
    /// </summary>
    public bool IsSolid { get; set; }

    /// <summary>
    ///     Whether this entity can be pushed by other entities.
    ///     Requires IsSolid to be true to have any effect.
    /// </summary>
    public bool IsPushable { get; set; }

    /// <summary>
    ///     Whether other entities can pass through this entity.
    ///     Overrides IsSolid for specific interactions (e.g., friendly NPCs).
    /// </summary>
    public bool AllowPassThrough { get; set; }

    /// <summary>
    ///     Creates a solid collision component that blocks movement.
    /// </summary>
    public static CollisionComponent Solid => new() { IsSolid = true };

    /// <summary>
    ///     Creates a pushable collision component.
    /// </summary>
    public static CollisionComponent Pushable => new() { IsSolid = true, IsPushable = true };

    /// <summary>
    ///     Creates a pass-through collision component (non-blocking).
    /// </summary>
    public static CollisionComponent PassThrough => new() { AllowPassThrough = true };
}
