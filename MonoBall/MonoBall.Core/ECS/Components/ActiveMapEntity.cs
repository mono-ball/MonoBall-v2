namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Tag component that marks entities as being in an active (loaded) map.
///     Entities with this component are in the current map or a connected map.
///     Used for query-level filtering to avoid iterating over entities in unloaded maps.
/// </summary>
/// <remarks>
///     This is a zero-size tag component used for efficient ECS queries.
///     The ActiveMapManagementSystem manages adding/removing this component
///     when maps are loaded/unloaded.
/// </remarks>
public struct ActiveMapEntity { }
