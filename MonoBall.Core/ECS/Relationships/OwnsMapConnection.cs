namespace MonoBall.Core.ECS.Relationships;

/// <summary>
///     Relationship type for map → connection ownership.
///     Used to link map connection entities to their parent map entity.
///     When the map entity is destroyed, Arch.Extended automatically removes all relationships.
/// </summary>
public struct OwnsMapConnection
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., connection priority, direction)
}
