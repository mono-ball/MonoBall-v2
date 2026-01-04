using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Scripting.Utilities;
using System.Linq;

/// <summary>
/// Wander behavior - NPC moves one tile in a random direction, waits, then repeats.
/// Uses event-driven architecture with MovementCompletedEvent, MovementBlockedEvent, and TimerElapsedEvent.
/// </summary>
public class WanderBehavior : ScriptBase
{
    private Direction _currentDirection = Direction.None;
    private int _blockedAttempts = 0;
    private (int X, int Y) _startPosition;
    private int _rangeX = 0; // 0 = no limit
    private int _rangeY = 0; // 0 = no limit
    private const string WaitTimerId = "wander_wait";

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        if (!Context.Entity.HasValue)
        {
            return;
        }
        
        // Get current position as start position
        var position = Context.GetComponent<PositionComponent>();
        _startPosition = (position.X, position.Y);
        
        // Get parameters
        var minWaitTime = GetParameterAsFloat("minWaitTime", 1.0f);
        var maxWaitTime = GetParameterAsFloat("maxWaitTime", 4.0f);
        var maxBlockedAttempts = GetParameterAsInt("maxBlockedAttempts", 4);
        _rangeX = GetParameterAsInt("rangeX", 0);
        _rangeY = GetParameterAsInt("rangeY", 0);
        
        StartWaitTimer();
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to movement completion to know when we've finished moving
        On<MovementCompletedEvent>(OnMovementCompleted);
        
        // Subscribe to movement blocked to handle obstacles
        On<MovementBlockedEvent>(OnMovementBlocked);
        
        // Subscribe to timer elapsed to know when wait period is over
        On<TimerElapsedEvent>(OnTimerElapsed);
        
    }

    private void OnMovementCompleted(MovementCompletedEvent evt)
    {
        // Only handle events for this entity
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        _currentDirection = Direction.None;
        _blockedAttempts = 0;
        StartWaitTimer();
    }

    private void OnMovementBlocked(MovementBlockedEvent evt)
    {
        // Only handle events for this entity
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        if (_currentDirection == Direction.None)
        {
            return; // Not trying to move, ignore
        }

        _blockedAttempts++;
        var maxBlockedAttempts = GetParameterAsInt("maxBlockedAttempts", 4);

        if (_blockedAttempts >= maxBlockedAttempts)
        {
            _currentDirection = Direction.None;
            _blockedAttempts = 0;
            StartWaitTimer();
            return;
        }

        // Try a new random direction immediately
        PickRandomDirection();
    }

    private void OnTimerElapsed(TimerElapsedEvent evt)
    {
        // Check if this is our wait timer
        if (!IsTimerEvent(WaitTimerId, evt))
        {
            return;
        }

        // Wait period is over, start movement
        PickRandomDirection();
    }

    public override void OnUnload()
    {
        CancelTimerIfExists(WaitTimerId);
        base.OnUnload();
    }
    
    private void PickRandomDirection()
    {
        if (!Context.Entity.HasValue)
        {
            return;
        }
        
        var position = Context.GetComponent<PositionComponent>();
        var availableDirections = GetAvailableDirections(position.X, position.Y);
        
        if (availableDirections.Count == 0)
        {
            StartWaitTimer();
            return;
        }
        
        _currentDirection = DirectionHelper.GetRandomDirection(availableDirections.ToArray());
        
        // Request movement
        Context.Apis.Movement.RequestMovement(Context.Entity.Value, _currentDirection);
    }
    
    private System.Collections.Generic.List<Direction> GetAvailableDirections(int currentX, int currentY)
    {
        var directions = new System.Collections.Generic.List<Direction>();
        
        // Check each direction to see if it's within range
        // North (decrease Y)
        if (_rangeY == 0 || (currentY - 1) >= (_startPosition.Y - _rangeY))
        {
            directions.Add(Direction.North);
        }
        
        // South (increase Y)
        if (_rangeY == 0 || (currentY + 1) <= (_startPosition.Y + _rangeY))
        {
            directions.Add(Direction.South);
        }
        
        // West (decrease X)
        if (_rangeX == 0 || (currentX - 1) >= (_startPosition.X - _rangeX))
        {
            directions.Add(Direction.West);
        }
        
        // East (increase X)
        if (_rangeX == 0 || (currentX + 1) <= (_startPosition.X + _rangeX))
        {
            directions.Add(Direction.East);
        }
        
        return directions;
    }

    private void StartWaitTimer()
    {
        // Get wait time parameters
        var minWaitTime = GetParameterAsFloat("minWaitTime", 1.0f);
        var maxWaitTime = GetParameterAsFloat("maxWaitTime", 4.0f);
        
        StartRandomTimer(WaitTimerId, minWaitTime, maxWaitTime, isRepeating: false);
    }
}
