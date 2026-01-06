using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Utilities;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for multi-parameter shader animation operations.
///     Animates multiple shader parameters simultaneously on an entity.
/// </summary>
public interface IShaderMultiParameterAnimationSystem
{
    /// <summary>
    ///     Sets animations for an entity.
    /// </summary>
    /// <param name="entity">The entity to animate.</param>
    /// <param name="animations">The animations to apply.</param>
    void SetAnimations(Entity entity, List<ShaderAnimationData> animations);

    /// <summary>
    ///     Clears animations for an entity.
    /// </summary>
    /// <param name="entity">The entity to clear animations for.</param>
    void ClearAnimations(Entity entity);
}
