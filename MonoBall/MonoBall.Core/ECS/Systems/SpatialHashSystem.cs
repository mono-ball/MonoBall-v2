using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that maintains a spatial hash of entity positions for O(1) collision queries.
///     Implements IEntityPositionService for use by CollisionService and other systems.
/// </summary>
/// <remarks>
///     <para>
///         This system rebuilds the spatial hash each frame for dynamic entities.
///         Static entities (like tiles) are not tracked - use ICollisionLayerCache for tile queries.
///     </para>
///     <para>
///         CRITICAL: ReadOnlySpan results are backed by pooled lists and are only valid until
///         the next call to any GetEntities* method. Do not store spans across calls.
///     </para>
/// </remarks>
public sealed class SpatialHashSystem
    : BaseSystem<World, float>,
        IEntityPositionService,
        IPrioritizedSystem
{
    // Query for entities with position and elevation
    private static readonly QueryDescription DynamicEntityQuery = new QueryDescription()
        .WithAll<PositionComponent, ElevationComponent>()
        .WithAny<NpcComponent, PlayerComponent>();

    private readonly IActiveMapFilterService _activeMapFilterService;
    private readonly ILogger _logger;

    // Pooled list for returning results without allocations
    private readonly List<Entity> _resultPool = new(16);

    // Spatial hash: mapId -> (x, y, elevation) -> entities
    private readonly Dictionary<
        string,
        Dictionary<(int X, int Y, int Elevation), List<Entity>>
    > _spatialHash = new();

    // List pool for reducing allocations
    private readonly Stack<List<Entity>> _listPool = new();

    /// <summary>
    ///     Initializes a new instance of SpatialHashSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="activeMapFilterService">The active map filter service for getting entity map IDs.</param>
    /// <param name="logger">The logger.</param>
    public SpatialHashSystem(
        World world,
        IActiveMapFilterService activeMapFilterService,
        ILogger logger
    )
        : base(world)
    {
        _activeMapFilterService =
            activeMapFilterService
            ?? throw new ArgumentNullException(nameof(activeMapFilterService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    ///     Runs early so collision queries have fresh data.
    /// </summary>
    public int Priority => SystemPriority.Movement - 10; // Run before movement

    /// <inheritdoc />
    public ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y, int elevation)
    {
        _resultPool.Clear();

        if (!_spatialHash.TryGetValue(mapId, out var mapHash))
            return ReadOnlySpan<Entity>.Empty;

        if (mapHash.TryGetValue((x, y, elevation), out var entities))
        {
            _resultPool.AddRange(entities);
        }

        return CollectionsMarshal.AsSpan(_resultPool);
    }

    /// <inheritdoc />
    public ReadOnlySpan<Entity> GetEntitiesAt(string mapId, int x, int y)
    {
        _resultPool.Clear();

        if (!_spatialHash.TryGetValue(mapId, out var mapHash))
            return ReadOnlySpan<Entity>.Empty;

        // Check all elevations at this position (0-15)
        for (var elevation = 0; elevation <= ElevationComponent.Max; elevation++)
        {
            if (mapHash.TryGetValue((x, y, elevation), out var entities))
            {
                _resultPool.AddRange(entities);
            }
        }

        return CollectionsMarshal.AsSpan(_resultPool);
    }

    /// <inheritdoc />
    public ReadOnlySpan<Entity> GetEntitiesInRange(
        string mapId,
        int centerX,
        int centerY,
        int range,
        int elevation
    )
    {
        _resultPool.Clear();

        if (!_spatialHash.TryGetValue(mapId, out var mapHash))
            return ReadOnlySpan<Entity>.Empty;

        // Manhattan distance range check
        for (var dx = -range; dx <= range; dx++)
        {
            for (var dy = -range; dy <= range; dy++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > range)
                    continue;

                var x = centerX + dx;
                var y = centerY + dy;

                if (mapHash.TryGetValue((x, y, elevation), out var entities))
                {
                    _resultPool.AddRange(entities);
                }
            }
        }

        return CollectionsMarshal.AsSpan(_resultPool);
    }

    /// <summary>
    ///     Rebuilds the spatial hash each frame.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Clear all existing entries (return lists to pool)
        foreach (var mapHash in _spatialHash.Values)
        {
            foreach (var list in mapHash.Values)
            {
                list.Clear();
                _listPool.Push(list);
            }
            mapHash.Clear();
        }

        // Query all dynamic entities and add to spatial hash
        World.Query(
            in DynamicEntityQuery,
            (Entity entity, ref PositionComponent position, ref ElevationComponent elevation) =>
            {
                // Defensive check: Verify entity is still alive
                // Entity might be destroyed or modified during query iteration (race condition)
                if (!World.IsAlive(entity))
                    return;

                // Get map ID from entity using the active map filter service
                // This works for both NPCs (via NpcComponent.MapId) and players (via tracked current map)
                var mapId = _activeMapFilterService.GetEntityMapId(entity);

                if (mapId == null)
                    return;

                // Get or create map hash
                if (!_spatialHash.TryGetValue(mapId, out var mapHash))
                {
                    mapHash = new Dictionary<(int X, int Y, int Elevation), List<Entity>>();
                    _spatialHash[mapId] = mapHash;
                }

                // Get or create position list
                var key = (position.X, position.Y, (int)elevation.Value);
                if (!mapHash.TryGetValue(key, out var entityList))
                {
                    entityList = _listPool.Count > 0 ? _listPool.Pop() : new List<Entity>(4);
                    mapHash[key] = entityList;
                }

                entityList.Add(entity);
            }
        );
    }

    /// <summary>
    ///     Clears the spatial hash for a specific map when it's unloaded.
    /// </summary>
    /// <param name="mapId">The map identifier.</param>
    public void UnloadMap(string mapId)
    {
        if (_spatialHash.TryGetValue(mapId, out var mapHash))
        {
            foreach (var list in mapHash.Values)
            {
                list.Clear();
                _listPool.Push(list);
            }
            mapHash.Clear();
            _spatialHash.Remove(mapId);
        }
    }
}
