using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for querying entity state and components.
///     Provides a focused interface for collision-related entity queries.
/// </summary>
public interface IEntityQueryService
{
    /// <summary>
    ///     Gets the tile position of an entity.
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <returns>The entity's tile position (X, Y).</returns>
    /// <exception cref="System.InvalidOperationException">Entity doesn't have PositionComponent.</exception>
    (int X, int Y) GetEntityPosition(Entity entity);

    /// <summary>
    ///     Checks if an entity is alive (valid reference in the world).
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity is alive, false otherwise.</returns>
    bool IsEntityAlive(Entity entity);

    /// <summary>
    ///     Checks if an entity has a specific component type.
    /// </summary>
    /// <typeparam name="T">The component type to check for.</typeparam>
    /// <param name="entity">The entity to check.</param>
    /// <returns>True if the entity has the component, false otherwise.</returns>
    bool HasComponent<T>(Entity entity)
        where T : struct;

    /// <summary>
    ///     Tries to get the CollisionComponent from an entity.
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <param name="component">The CollisionComponent if found.</param>
    /// <returns>True if the entity has a CollisionComponent, false otherwise.</returns>
    bool TryGetCollisionComponent(Entity entity, out CollisionComponent component);

    /// <summary>
    ///     Gets the map ID for an entity if it has one.
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <returns>The map ID, or null if the entity has no map association.</returns>
    string? GetEntityMapId(Entity entity);
}
