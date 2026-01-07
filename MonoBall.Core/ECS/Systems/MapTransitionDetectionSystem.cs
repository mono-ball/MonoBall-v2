using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Services;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that detects when the player crosses map boundaries and fires MapTransitionEvent.
///     This system runs each frame and checks which map the player is currently in.
///     Note: GameEnteredEvent is NOT handled here - it's fired from GameInitializationService.
/// </summary>
public class MapTransitionDetectionSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    private readonly IActiveMapFilterService _activeMapFilterService;
    private readonly ILogger _logger;
    private bool _isInitialized;
    private string? _previousPlayerMapId;

    /// <summary>
    ///     Initializes a new instance of the MapTransitionDetectionSystem.
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
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.MapTransitionDetection;

    /// <summary>
    ///     Updates the system, detecting map transitions.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Get player's current map
        var currentPlayerMapId = _activeMapFilterService.GetPlayerCurrentMapId();

        // Skip if player not found or not in any map
        if (string.IsNullOrEmpty(currentPlayerMapId))
            return;

        // On first update, just initialize the tracking
        if (!_isInitialized)
        {
            _previousPlayerMapId = currentPlayerMapId;
            _isInitialized = true;
            _logger.Debug(
                "Initialized MapTransitionDetectionSystem with player in map {MapId}",
                currentPlayerMapId
            );
            return;
        }

        // Check if player has transitioned to a different map (by walking, not by warp)
        // Warps are handled by MapConnectionSystem which fires its own MapTransitionEvent
        if (_previousPlayerMapId != null && _previousPlayerMapId != currentPlayerMapId)
        {
            // Player has transitioned to a new map - fire event
            var transitionEvent = new MapTransitionEvent
            {
                SourceMapId = _previousPlayerMapId,
                TargetMapId = currentPlayerMapId,
                Direction = MapConnectionDirection.North, // Default direction
                Offset = 0,
            };
            EventBus.Send(ref transitionEvent);
            _logger.Information(
                "Player walked from {SourceMapId} to {TargetMapId}",
                _previousPlayerMapId,
                currentPlayerMapId
            );
        }

        // Update previous map ID
        _previousPlayerMapId = currentPlayerMapId;
    }
}
