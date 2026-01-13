namespace MonoBall.Core.ECS.Relationships;

/// <summary>
///     Relationship type for map → NPC ownership.
///     Used to link NPC entities to their parent map entity.
///     When the map entity is destroyed, Arch.Extended automatically removes all relationships.
/// </summary>
public struct OwnsNpc
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., NPC spawn order, priority)
}
