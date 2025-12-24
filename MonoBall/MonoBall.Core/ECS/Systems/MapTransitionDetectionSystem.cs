using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Services;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that detects when the player crosses map boundaries and fires MapTransitionEvent.
    /// This system runs each frame and checks which map the player is currently in.
    /// </summary>
    public class MapTransitionDetectionSystem : BaseSystem<World, float>
    {
        private readonly IActiveMapFilterService _activeMapFilterService;
        private readonly ILogger _logger;
        private string? _previousPlayerMapId = null;
        private bool _isInitialized = false;
        private bool _hasRenderedFirstFrame = false;

        /// <summary>
        /// Initializes a new instance of the MapTransitionDetectionSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="activeMapFilterService">The active map filter service.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public MapTransitionDetectionSystem(
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
        /// Updates the system, detecting map transitions.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Get player's current map
            string? currentPlayerMapId = _activeMapFilterService.GetPlayerCurrentMapId();

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
    }
}
