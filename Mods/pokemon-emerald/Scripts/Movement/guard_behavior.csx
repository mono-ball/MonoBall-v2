using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Scripting.Utilities;

/// <summary>
/// Guard behavior - NPC stays at position, scans for threats by rotating direction.
/// Uses event-driven architecture with MovementCompletedEvent and TimerElapsedEvent.
/// </summary>
public class GuardBehavior : ScriptBase
{
    private (int X, int Y) _guardPosition;
    private Direction _facingDirection = Direction.South;
    private const string ScanTimerId = "guard_scan_timer";
    private float _minInterval = 1.5f;
    private float _maxInterval = 3.0f;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        if (!Context.Entity.HasValue)
        {
            return;
        }
        
        // Get guard position from current position
        var position = Context.GetComponent<PositionComponent>();
        _guardPosition = (position.X, position.Y);
        
        // Get current facing direction
        _facingDirection = TryGetFacingDirection() ?? Direction.South;
        
        // Randomize initial scan timer to prevent synchronization
        StartRandomTimer(ScanTimerId, _minInterval, _maxInterval, isRepeating: true);
        
        Context.Logger.Information(
            "Guard activated at position ({X}, {Y})",
            _guardPosition.X,
            _guardPosition.Y
        );
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to movement completion to return to guard position if moved
        On<MovementCompletedEvent>(OnMovementCompleted);
        
        // Subscribe to timer elapsed to rotate scan direction
        On<TimerElapsedEvent>(OnTimerElapsed);
        
        Context.Logger.Debug("Guard behavior: Event handlers registered");
    }

    private void OnMovementCompleted(MovementCompletedEvent evt)
    {
        // Only handle events for this entity
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        var position = Context.GetComponent<PositionComponent>();

        // Return to guard position if moved
        if (position.X != _guardPosition.X || position.Y != _guardPosition.Y)
        {
            var direction = DirectionHelper.GetDirectionTo(position.X, position.Y, _guardPosition.X, _guardPosition.Y);
            
            // Request movement back to guard position
            // IsEventForThisEntity guarantees Context.Entity.HasValue is true
            Context.Apis.Movement.RequestMovement(Context.Entity.Value, direction);
            
            Context.Logger.Debug(
                "Guard returning to post from ({X}, {Y}) to ({GuardX}, {GuardY})",
                position.X,
                position.Y,
                _guardPosition.X,
                _guardPosition.Y
            );
        }
    }

    private void OnTimerElapsed(TimerElapsedEvent evt)
    {
        // Check if this is our scan timer
        if (!IsTimerEvent(ScanTimerId, evt))
        {
            return;
        }

        // Rotate scan direction clockwise
        _facingDirection = DirectionHelper.RotateClockwise(_facingDirection);
        
        // Update facing direction
        SetFacingDirection(_facingDirection);
        
        // Set new random interval for next scan
        // Note: For repeating timers, we need to cancel and restart with new interval
        // since repeating timers use the same duration each cycle
        CancelTimer(ScanTimerId);
        StartRandomTimer(ScanTimerId, _minInterval, _maxInterval, isRepeating: true);
        
        Context.Logger.Debug("Guard facing {Direction}", _facingDirection);
    }

    public override void OnUnload()
    {
        // Cancel any active timers
        CancelTimerIfExists(ScanTimerId);
        
        Context.Logger.Information("Guard deactivated");
        base.OnUnload();
    }
}
