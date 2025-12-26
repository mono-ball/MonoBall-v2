using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Scripting.Utilities;

/// <summary>
/// Patrol behavior - NPC walks along waypoints in a loop, pausing briefly at each point.
/// Uses event-driven architecture with MovementCompletedEvent and TimerElapsedEvent.
/// Requires MovementRoute component with waypoints.
/// </summary>
public class PatrolBehavior : ScriptBase
{
    private int _currentWaypoint = -1;
    private float _waitDuration = 0f;
    private const string WaitTimerId = "patrol_wait";
    private bool _isWaiting = false;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Get parameters
        _waitDuration = GetParameterAsFloat("pauseAtWaypoint", 1.0f);
        
        // Check if entity has MovementRoute component (waypoints)
        if (!Context.Entity.HasValue || !Context.HasComponent<MovementRoute>())
        {
            Context.Logger.Warning(
                "PatrolBehavior requires MovementRoute component. Add 'waypoints' property to map object. Deactivating behavior."
            );
            _currentWaypoint = -1; // Mark as invalid
            return;
        }

        var route = Context.GetComponent<MovementRoute>();
        if (route.Waypoints == null || route.Waypoints.Length == 0)
        {
            Context.Logger.Warning("PatrolBehavior: MovementRoute has no waypoints. Deactivating behavior.");
            _currentWaypoint = -1;
            return;
        }

        _currentWaypoint = 0;
        
        // Randomize initial wait timer to prevent synchronization
        if (_waitDuration > 0)
        {
            _isWaiting = true;
            StartRandomTimer(WaitTimerId, 0f, _waitDuration, isRepeating: false);
        }
        else
        {
            // Start moving immediately
            MoveToWaypoint();
        }
        
        Context.Logger.Information(
            "Patrol initialized | waypoints: {Count}, loop: {Loop}, wait: {Wait}s",
            route.Waypoints.Length,
            route.Loop,
            _waitDuration
        );
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to movement completion to know when we've reached a waypoint
        On<MovementCompletedEvent>(OnMovementCompleted);
        
        // Subscribe to timer elapsed to know when wait period is over
        On<TimerElapsedEvent>(OnTimerElapsed);
        
        Context.Logger.Debug("Patrol behavior: Event handlers registered");
    }

    private void OnMovementCompleted(MovementCompletedEvent evt)
    {
        // Only handle events for this entity
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        if (_currentWaypoint < 0)
        {
            return; // Invalid state
        }

        var route = Context.GetComponent<MovementRoute>();
        if (route.Waypoints == null || route.Waypoints.Length == 0)
        {
            return;
        }

        var position = Context.GetComponent<PositionComponent>();
        var target = route.Waypoints[_currentWaypoint];

        // Check if we reached the waypoint
        if (position.X == target.X && position.Y == target.Y)
        {
            Context.Logger.Debug(
                "Reached waypoint {Index}/{Total}: ({X},{Y})",
                _currentWaypoint,
                route.Waypoints.Length - 1,
                target.X,
                target.Y
            );

            // Move to next waypoint
            _currentWaypoint++;
            if (_currentWaypoint >= route.Waypoints.Length)
            {
                _currentWaypoint = route.Loop ? 0 : route.Waypoints.Length - 1;
            }

            // Start wait timer before moving to next waypoint
            if (_waitDuration > 0)
            {
                _isWaiting = true;
                // Add ±20% variation to wait duration
                var variation = _waitDuration * 0.2f;
                var minWait = _waitDuration - variation;
                var maxWait = _waitDuration + variation;
                StartRandomTimer(WaitTimerId, minWait, maxWait, isRepeating: false);
            }
            else
            {
                // No wait, move immediately
                MoveToWaypoint();
            }
        }
    }

    private void OnTimerElapsed(TimerElapsedEvent evt)
    {
        // Check if this is our wait timer
        if (!IsTimerEvent(WaitTimerId, evt))
        {
            return;
        }

        _isWaiting = false;
        MoveToWaypoint();
    }

    public override void OnUnload()
    {
        // Cancel any active timers
        CancelTimerIfExists(WaitTimerId);
        
        Context.Logger.Debug("Patrol behavior deactivated");
        base.OnUnload();
    }

    private void MoveToWaypoint()
    {
        if (_currentWaypoint < 0 || !Context.Entity.HasValue)
        {
            return;
        }

        var route = Context.GetComponent<MovementRoute>();
        if (route.Waypoints == null || _currentWaypoint >= route.Waypoints.Length)
        {
            return;
        }

        var position = Context.GetComponent<PositionComponent>();
        var target = route.Waypoints[_currentWaypoint];

        // Calculate direction to waypoint
        var direction = DirectionHelper.GetDirectionTo(position.X, position.Y, target.X, target.Y);

        // Request movement
        Context.Apis.Movement.RequestMovement(Context.Entity.Value, direction);
        
        Context.Logger.Debug(
            "Moving to waypoint {Index}: ({X},{Y})",
            _currentWaypoint,
            target.X,
            target.Y
        );
    }
}
