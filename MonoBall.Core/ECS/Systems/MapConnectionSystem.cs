using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System responsible for managing map connections and transitions.
/// </summary>
public class MapConnectionSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    private readonly QueryDescription _connectionQueryDescription;
    private readonly ILogger _logger;

    /// <summary>
    ///     Initializes a new instance of the MapConnectionSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public MapConnectionSystem(World world, ILogger logger)
        : base(world)
    {
        _connectionQueryDescription = new QueryDescription().WithAll<
            MapComponent,
            MapConnectionComponent
        >();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.MapConnection;

    /// <summary>
    ///     Transitions from one map to another via a connection.
    /// </summary>
    /// <param name="sourceMapId">The source map ID.</param>
    /// <param name="targetMapId">The target map ID.</param>
    /// <param name="direction">The direction of the transition.</param>
    /// <param name="offset">The offset in tiles.</param>
    public void TransitionToMap(
        string sourceMapId,
        string targetMapId,
        MapConnectionDirection direction,
        int offset
    )
    {
        if (string.IsNullOrEmpty(sourceMapId) || string.IsNullOrEmpty(targetMapId))
        {
            _logger.Warning("Attempted transition with null or empty map IDs");
            return;
        }

        _logger.Information(
            "Transitioning from {SourceMap} to {TargetMap} ({Direction}, offset: {Offset})",
            sourceMapId,
            targetMapId,
            direction,
            offset
        );

        // Fire MapTransitionEvent
        var transitionEvent = new MapTransitionEvent
        {
            SourceMapId = sourceMapId,
            TargetMapId = targetMapId,
            Direction = direction,
            Offset = offset,
        };
        EventBus.Send(ref transitionEvent);

        // Additional transition logic can be added here
        // For example: camera movement, player position updates, etc.
    }

    /// <summary>
    ///     Gets the connection information for a map in a specific direction.
    /// </summary>
    /// <param name="mapId">The map ID.</param>
    /// <param name="direction">The direction.</param>
    /// <returns>The connection component, or null if not found.</returns>
    public MapConnectionComponent? GetConnection(string mapId, MapConnectionDirection direction)
    {
        if (string.IsNullOrEmpty(mapId))
            return null;

        MapConnectionComponent? foundConnection = null;

        World.Query(
            in _connectionQueryDescription,
            (ref MapComponent mapComp, ref MapConnectionComponent connComp) =>
            {
                if (mapComp.MapId == mapId && connComp.Direction == direction)
                    foundConnection = connComp;
            }
        );

        return foundConnection;
    }

    /// <summary>
    ///     Calculates the world position for a connected map based on the connection offset.
    /// </summary>
    /// <param name="sourceMapId">The source map ID.</param>
    /// <param name="sourceMapWidth">The source map width in tiles.</param>
    /// <param name="sourceMapHeight">The source map height in tiles.</param>
    /// <param name="tileWidth">The tile width in pixels.</param>
    /// <param name="tileHeight">The tile height in pixels.</param>
    /// <param name="direction">The connection direction.</param>
    /// <param name="offset">The offset in tiles.</param>
    /// <returns>The world position for the connected map.</returns>
    public Vector2 CalculateConnectedMapPosition(
        string sourceMapId,
        int sourceMapWidth,
        int sourceMapHeight,
        int tileWidth,
        int tileHeight,
        MapConnectionDirection direction,
        int offset
    )
    {
        var position = Vector2.Zero;

        switch (direction)
        {
            case MapConnectionDirection.North:
                position = new Vector2(offset * tileWidth, -sourceMapHeight * tileHeight);
                break;

            case MapConnectionDirection.South:
                position = new Vector2(offset * tileWidth, sourceMapHeight * tileHeight);
                break;

            case MapConnectionDirection.East:
                position = new Vector2(sourceMapWidth * tileWidth, offset * tileHeight);
                break;

            case MapConnectionDirection.West:
                position = new Vector2(-sourceMapWidth * tileWidth, offset * tileHeight);
                break;
        }

        return position;
    }
}
