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
        
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<MovementCompletedEvent>(OnMovementCompleted);
        On<TimerElapsedEvent>(OnTimerElapsed);
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

        if (position.X == target.X && position.Y == target.Y)
        {
            _currentWaypoint++;
            if (_currentWaypoint >= route.Waypoints.Length)
            {
                _currentWaypoint = route.Loop ? 0 : route.Waypoints.Length - 1;
            }

            if (_waitDuration > 0)
            {
                _isWaiting = true;
                var variation = _waitDuration * 0.2f;
                StartRandomTimer(WaitTimerId, _waitDuration - variation, _waitDuration + variation, isRepeating: false);
            }
            else
            {
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
        CancelTimerIfExists(WaitTimerId);
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

        Context.Apis.Movement.RequestMovement(Context.Entity.Value, direction);
    }
}
