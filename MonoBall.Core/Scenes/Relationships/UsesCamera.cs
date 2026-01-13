namespace MonoBall.Core.Scenes.Relationships;

/// <summary>
///     Relationship type for scene → camera entity association.
///     Used when <see cref="MonoBall.Core.Scenes.Components.SceneComponent.CameraMode"/> is set to
///     <see cref="MonoBall.Core.Scenes.SceneCameraMode.SceneCamera"/>.
/// </summary>
/// <remarks>
///     <para>
///         This relationship establishes a one-to-one association between a scene entity and a camera entity.
///         When a scene uses SceneCamera mode, it must have exactly one camera entity linked via this relationship.
///     </para>
///     <para>
///         <strong>Cardinality:</strong> One-to-one. Each scene should have at most one camera relationship.
///         Each camera entity can be associated with multiple scenes, but typically each scene has its own camera.
///     </para>
///     <para>
///         <strong>Creation:</strong> The relationship is created automatically when a scene is created via
///         <see cref="MonoBall.Core.Scenes.ISceneManager.CreateScene"/> with SceneCamera mode and a camera entity parameter.
///         The relationship is validated to ensure both entities are alive and the camera entity has a
///         <see cref="MonoBall.Core.ECS.Components.CameraComponent"/>.
///     </para>
///     <para>
///         <strong>Cleanup:</strong> The relationship is automatically removed when either the scene entity or
///         the camera entity is destroyed. This follows the same pattern as other ownership relationships
///         like <see cref="MonoBall.Core.UI.Relationships.OwnsUIElement"/>.
///     </para>
///     <para>
///         <strong>Querying:</strong> Use <see cref="Arch.Core.World.GetRelationships{T}"/> to query camera
///         relationships for a scene entity. Always validate that entities are alive and have required components
///         before querying relationships to avoid exceptions.
///     </para>
///     <para>
///         <strong>Extension:</strong> This is currently a marker relationship with no data. It can be extended
///         with metadata if needed (e.g., priority, viewport override, camera constraints).
///     </para>
/// </remarks>
public struct UsesCamera
{
    // Marker relationship - no data needed
    // Can be extended with metadata if needed (e.g., priority, viewport override)
}
