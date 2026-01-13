namespace MonoBall.Core.ECS.Relationships;

/// <summary>
///     Relationship type for map → tile chunk ownership.
///     Used to link tile chunk entities to their parent map entity.
///     When the map entity is destroyed, Arch.Extended automatically removes all relationships.
/// </summary>
public struct OwnsTileChunk
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., chunk priority, layer index)
}
