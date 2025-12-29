using Arch.Core;

namespace MonoBall.Core.Scenes.Components;

/// <summary>
///     Component that explicitly links an entity to a scene.
///     Used by windows, shaders, and other scene-scoped entities to establish scene membership.
/// </summary>
/// <remarks>
///     <para>
///         This component provides an explicit, queryable relationship between entities and scenes.
///         It allows systems to efficiently determine which scene an entity belongs to without
///         hardcoding knowledge of specific component types.
///     </para>
///     <para>
///         Examples:
///         - Window entities (message boxes, popups) belong to their scene
///         - Shader entities can be scoped to a specific scene
///         - UI elements that are scene-specific
///     </para>
/// </remarks>
public struct SceneOwnershipComponent
{
    /// <summary>
    ///     Gets or sets the scene entity this entity belongs to.
    /// </summary>
    public Entity SceneEntity { get; set; }
}
