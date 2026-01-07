using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for shader parameter timeline operations.
///     Animates shader parameters using keyframe-based timelines.
/// </summary>
public interface IShaderParameterTimelineSystem
{
    /// <summary>
    ///     Adds keyframes for an entity's timeline.
    /// </summary>
    /// <param name="entity">The entity with the timeline component.</param>
    /// <param name="keyframes">The list of keyframes to add.</param>
    void AddKeyframes(Entity entity, List<ShaderParameterKeyframe> keyframes);

    /// <summary>
    ///     Removes keyframes for an entity (called when component is removed).
    /// </summary>
    /// <param name="entity">The entity.</param>
    void RemoveKeyframes(Entity entity);
}
