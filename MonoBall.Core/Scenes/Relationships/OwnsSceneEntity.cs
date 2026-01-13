namespace MonoBall.Core.Scenes.Relationships;

/// <summary>
///     Relationship type for scene → entity ownership.
///     Used to link scene-scoped entities (windows, shaders, etc.) to their parent scene entity.
///     When the scene entity is destroyed, Arch.Extended automatically removes all relationships.
/// </summary>
public struct OwnsSceneEntity
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., ZOrder, ElementType)
}
