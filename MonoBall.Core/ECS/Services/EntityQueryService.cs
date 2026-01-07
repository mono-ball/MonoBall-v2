using System;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using Serilog;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Service for querying entity state and components.
///     Wraps World access for collision-related entity queries.
/// </summary>
public sealed class EntityQueryService : IEntityQueryService
{
    private readonly ILogger _logger;
    private readonly World _world;

    /// <summary>
    ///     Initializes a new instance of EntityQueryService.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="logger">The logger.</param>
    public EntityQueryService(World world, ILogger logger)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public (int X, int Y) GetEntityPosition(Entity entity)
    {
        if (!_world.TryGet<PositionComponent>(entity, out var position))
            throw new InvalidOperationException(
                $"Entity {entity.Id} does not have PositionComponent."
            );

        return (position.X, position.Y);
    }

    /// <inheritdoc />
    public bool IsEntityAlive(Entity entity)
    {
        return _world.IsAlive(entity);
    }

    /// <inheritdoc />
    public bool HasComponent<T>(Entity entity)
        where T : struct
    {
        return _world.Has<T>(entity);
    }

    /// <inheritdoc />
    public bool TryGetCollisionComponent(Entity entity, out CollisionComponent component)
    {
        return _world.TryGet(entity, out component);
    }

    /// <inheritdoc />
    public string? GetEntityMapId(Entity entity)
    {
        // Check NPCs first (most common)
        if (_world.TryGet<NpcComponent>(entity, out var npcComponent))
            return npcComponent.MapId;

        // Check MapComponent
        if (_world.TryGet<MapComponent>(entity, out var mapComponent))
            return mapComponent.MapId;

        // Check PlayerComponent - player map is determined by position, not component
        // For now, return null and let caller use IActiveMapFilterService if needed
        return null;
    }
}
