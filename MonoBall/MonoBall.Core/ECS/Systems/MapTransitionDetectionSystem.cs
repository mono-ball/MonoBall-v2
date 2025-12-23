using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that detects when the player crosses map boundaries and fires MapTransitionEvent.
    /// This system runs each frame and checks which map the player is currently in.
    /// </summary>
    public class MapTransitionDetectionSystem : BaseSystem<World, float>
    {
        private readonly ILogger _logger;
        private string? _previousPlayerMapId = null;
        private bool _isInitialized = false;
        private bool _hasRenderedFirstFrame = false;

        // Cached query descriptions
        private readonly QueryDescription _playerQuery = new QueryDescription().WithAll<
            PlayerComponent,
            PositionComponent
        >();

        private readonly QueryDescription _mapQuery = new QueryDescription().WithAll<
            MapComponent,
            PositionComponent
        >();

        /// <summary>
        /// Initializes a new instance of the MapTransitionDetectionSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public MapTransitionDetectionSystem(World world, ILogger logger)
            : base(world)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Updates the system, detecting map transitions.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Get player's current map
            string? currentPlayerMapId = GetPlayerCurrentMapId();

            // Skip if player not found or not in any map
            if (string.IsNullOrEmpty(currentPlayerMapId))
            {
                return;
            }

            // On first update, initialize but don't fire event yet
            // Wait until after first frame is rendered to ensure popup animation starts correctly
            if (!_isInitialized)
            {
                _previousPlayerMapId = currentPlayerMapId;
                _isInitialized = true;
                _logger.Debug(
                    "Initialized with player in map {MapId}, waiting for first frame",
                    currentPlayerMapId
                );
                return;
            }

            // After first frame is rendered, fire GameEnteredEvent for initial game entry
            // This ensures popup animation starts correctly (popup created in Update, rendered in Draw)
            if (!_hasRenderedFirstFrame && _isInitialized)
            {
                _hasRenderedFirstFrame = true;
                var gameEnteredEvent = new GameEnteredEvent { InitialMapId = currentPlayerMapId };
                EventBus.Send(ref gameEnteredEvent);
                _logger.Information(
                    "Fired GameEnteredEvent for initial map {MapId} (after first frame)",
                    currentPlayerMapId
                );
                return;
            }

            // Check if player has transitioned to a different map
            if (_previousPlayerMapId != null && _previousPlayerMapId != currentPlayerMapId)
            {
                // Player has transitioned to a new map - fire event
                var transitionEvent = new MapTransitionEvent
                {
                    SourceMapId = _previousPlayerMapId,
                    TargetMapId = currentPlayerMapId,
                    Direction = MapConnectionDirection.North, // Default direction (could be improved)
                    Offset = 0,
                };
                EventBus.Send(ref transitionEvent);
                _logger.Information(
                    "Player transitioned from {SourceMapId} to {TargetMapId}",
                    _previousPlayerMapId,
                    currentPlayerMapId
                );
            }

            // Update previous map ID
            _previousPlayerMapId = currentPlayerMapId;
        }

        /// <summary>
        /// Gets the map ID that the player is currently positioned in.
        /// </summary>
        /// <returns>The map ID containing the player, or null if player not found or not in any map.</returns>
        private string? GetPlayerCurrentMapId()
        {
            // Query for player entity
            Vector2? playerPixelPos = null;
            World.Query(
                in _playerQuery,
                (Entity entity, ref PositionComponent position) =>
                {
                    playerPixelPos = new Vector2(position.PixelX, position.PixelY);
                }
            );

            if (!playerPixelPos.HasValue)
            {
                return null;
            }

            // Find which map contains the player
            string? playerMapId = null;
            World.Query(
                in _mapQuery,
                (Entity entity, ref MapComponent map, ref PositionComponent mapPosition) =>
                {
                    // If we already found a map, skip remaining maps (return first match)
                    if (playerMapId != null)
                    {
                        return;
                    }

                    // Calculate map bounds in pixels
                    float mapLeft = mapPosition.Position.X;
                    float mapTop = mapPosition.Position.Y;
                    float mapRight = mapLeft + (map.Width * map.TileWidth);
                    float mapBottom = mapTop + (map.Height * map.TileHeight);

                    // Check if player is within map bounds
                    if (
                        playerPixelPos.Value.X >= mapLeft
                        && playerPixelPos.Value.X < mapRight
                        && playerPixelPos.Value.Y >= mapTop
                        && playerPixelPos.Value.Y < mapBottom
                    )
                    {
                        playerMapId = map.MapId;
                    }
                }
            );

            return playerMapId;
        }
    }
}
