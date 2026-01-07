using System;
using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for querying and modifying entity elevation.
///     Wraps World access for elevation-related entity queries.
/// </summary>
public sealed class EntityElevationService : IEntityElevationService
{
    private readonly World _world;

    /// <summary>
    ///     Initializes a new instance of EntityElevationService.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    public EntityElevationService(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <inheritdoc />
    public byte GetEntityElevation(Entity entity)
    {
        if (!_world.TryGet<ElevationComponent>(entity, out var elevation))
            throw new InvalidOperationException(
                $"Entity {entity.Id} does not have ElevationComponent. "
                    + "All movable entities must have ElevationComponent."
            );

        return elevation.Value;
    }

    /// <inheritdoc />
    public void SetEntityElevation(Entity entity, byte elevation)
    {
        if (!_world.Has<ElevationComponent>(entity))
            throw new InvalidOperationException(
                $"Entity {entity.Id} does not have ElevationComponent. "
                    + "Cannot set elevation on entity without ElevationComponent."
            );

        _world.Set(entity, new ElevationComponent { Value = elevation });
    }

    /// <inheritdoc />
    public bool TryGetEntityElevation(Entity entity, out byte elevation)
    {
        if (_world.TryGet<ElevationComponent>(entity, out var elevationComponent))
        {
            elevation = elevationComponent.Value;
            return true;
        }

        elevation = 0;
        return false;
    }

    /// <inheritdoc />
    public bool TryGetElevationComponent(Entity entity, out ElevationComponent component)
    {
        return _world.TryGet(entity, out component);
    }
}
