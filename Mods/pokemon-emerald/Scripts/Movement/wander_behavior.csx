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
    private int _movementCount = 0;
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
        
        // Get current position
        var position = Context.GetComponent<PositionComponent>();
        
        // Load persisted state
        _movementCount = Get<int>("movementCount", 0);
        var hasSavedStart = Get<bool>("hasSavedStart", false);
        
        if (hasSavedStart)
        {
            // Use saved start position (for hot-reload or persistence)
            _startPosition = GetPositionState("startX", "startY", position.X, position.Y);
        }
        else
        {
            // First initialization - use current position as start
            _startPosition = (position.X, position.Y);
            SetPositionState("startX", "startY", _startPosition.X, _startPosition.Y);
            Set("hasSavedStart", true);
        }
        
        // Get parameters
        var minWaitTime = GetParameterAsFloat("minWaitTime", 1.0f);
        var maxWaitTime = GetParameterAsFloat("maxWaitTime", 4.0f);
        var maxBlockedAttempts = GetParameterAsInt("maxBlockedAttempts", 4);
        _rangeX = GetParameterAsInt("rangeX", 0);
        _rangeY = GetParameterAsInt("rangeY", 0);
        
        Context.Logger.Information(
            "Wander behavior initialized. Parameters: minWait={MinWait}s, maxWait={MaxWait}s, maxBlocked={MaxBlocked}, rangeX={RangeX}, rangeY={RangeY}",
            minWaitTime,
            maxWaitTime,
            maxBlockedAttempts,
            _rangeX,
            _rangeY
        );
        
        // Start initial wait timer before first movement
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
        
        Context.Logger.Debug("Wander behavior: Event handlers registered");
    }

    private void OnMovementCompleted(MovementCompletedEvent evt)
    {
        // Only handle events for this entity
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        _movementCount++;
        _currentDirection = Direction.None;
        _blockedAttempts = 0; // Reset blocked counter on successful move
        
        SaveState();
        
        Context.Logger.Debug(
            "Wander completed move #{Count} to ({X}, {Y})",
            _movementCount,
            evt.NewPosition.X,
            evt.NewPosition.Y
        );
        
        // Start wait timer before next movement
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

        Context.Logger.Debug(
            "Wander blocked (attempt {Attempt}/{Max}). Reason: {Reason}",
            _blockedAttempts,
            maxBlockedAttempts,
            evt.BlockReason
        );

        // After max blocked attempts, give up and wait
        if (_blockedAttempts >= maxBlockedAttempts)
        {
            Context.Logger.Information(
                "Wander stuck after {Attempts} attempts - waiting before retry",
                _blockedAttempts
            );

            _currentDirection = Direction.None;
            _blockedAttempts = 0;
            
            SaveState();
            
            // Start wait timer before trying again
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
        // Cancel any active timers
        CancelTimerIfExists(WaitTimerId);
        
        SaveState();
        Context.Logger.Information(
            "Wander behavior unloaded. Total movements: {Count}",
            _movementCount
        );
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
            // No valid directions, wait and try again
            Context.Logger.Debug("Wander: No valid directions available, waiting");
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
        
        // Start random timer (non-repeating)
        StartRandomTimer(WaitTimerId, minWaitTime, maxWaitTime, isRepeating: false);
        
        Context.Logger.Debug("Wander: Started wait timer for {MinWait}-{MaxWait}s", minWaitTime, maxWaitTime);
    }

    private void SaveState()
    {
        Set("movementCount", _movementCount);
        Set("currentDirection", _currentDirection.ToString());
        Set("blockedAttempts", _blockedAttempts);
        SetPositionState("startX", "startY", _startPosition.X, _startPosition.Y);
    }
}
