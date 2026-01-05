using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for querying and modifying entity elevation.
///     Focused interface following Interface Segregation Principle.
/// </summary>
/// <remarks>
///     This interface is separated from IEntityQueryService to keep
///     collision-related queries focused and to allow different
///     implementations for elevation handling if needed.
/// </remarks>
public interface IEntityElevationService
{
    /// <summary>
    ///     Gets the elevation for an entity.
    ///     Requires ElevationComponent - all entities must have this component.
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <returns>Entity elevation (0-15).</returns>
    /// <exception cref="System.InvalidOperationException">Thrown if entity doesn't have ElevationComponent (fail fast).</exception>
    byte GetEntityElevation(Entity entity);

    /// <summary>
    ///     Sets the elevation for an entity.
    ///     Updates ElevationComponent on the entity.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="elevation">The new elevation value (0-15).</param>
    /// <exception cref="System.InvalidOperationException">Thrown if entity doesn't have ElevationComponent (fail fast).</exception>
    void SetEntityElevation(Entity entity, byte elevation);

    /// <summary>
    ///     Tries to get the elevation of an entity.
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <param name="elevation">The elevation value if found.</param>
    /// <returns>True if the entity has an ElevationComponent, false otherwise.</returns>
    bool TryGetEntityElevation(Entity entity, out byte elevation);

    /// <summary>
    ///     Tries to get an ElevationComponent from an entity.
    /// </summary>
    /// <param name="entity">The entity to query.</param>
    /// <param name="component">When this method returns, contains the ElevationComponent if found; otherwise, the default value.</param>
    /// <returns>True if entity has ElevationComponent, false otherwise.</returns>
    bool TryGetElevationComponent(Entity entity, out ElevationComponent component);
}
