using Arch.Core;

namespace MonoBall.Core.Scripting.Runtime;

/// <summary>
///     Interface for creating and destroying entities in the ECS world.
///     Available to plugin scripts for dynamic entity management.
/// </summary>
public interface IEntityFactory
{
    /// <summary>
    ///     Creates a new entity with the specified components.
    /// </summary>
    /// <param name="components">The components to add to the entity (must be struct components).</param>
    /// <returns>The created entity.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown if components is null.</exception>
    /// <exception cref="System.ArgumentException">Thrown if any component is not a value type (struct).</exception>
    Entity CreateEntity(params object[] components);

    /// <summary>
    ///     Destroys an entity.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    /// <exception cref="System.InvalidOperationException">Thrown if the entity is not alive.</exception>
    void DestroyEntity(Entity entity);
}
