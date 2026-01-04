using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using System.Linq;

/// <summary>
/// Look Around behavior - NPC periodically changes facing direction.
/// Uses event-driven architecture with TimerElapsedEvent.
/// </summary>
public class LookAroundBehavior : ScriptBase
{
    private Direction[] _directions = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
    private int _currentDirectionIndex = 0;
    private const string LookTimerId = "look_around_timer";
    private float _minInterval = 1.5f;
    private float _maxInterval = 3.0f;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Get parameters
        var lookInterval = GetParameterAsFloat("lookInterval", 2.0f);
        _minInterval = lookInterval * 0.75f; // 75% of base
        _maxInterval = lookInterval * 1.5f;  // 150% of base
        
        // Parse directions parameter if provided
        var parsedDirections = GetParameterAsDirections("directions", null);
        if (parsedDirections.Length > 0)
        {
            _directions = parsedDirections;
        }
        
        // Get current facing direction
        var currentFacing = TryGetFacingDirection() ?? Direction.South;
        
        // Find initial direction index
        var index = System.Array.IndexOf(_directions, currentFacing);
        if (index >= 0)
        {
            _currentDirectionIndex = index;
        }
        
        // Randomize initial timer to prevent synchronization
        StartRandomTimer(LookTimerId, _minInterval, _maxInterval, isRepeating: true);
        
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<TimerElapsedEvent>(OnTimerElapsed);
    }

    private void OnTimerElapsed(TimerElapsedEvent evt)
    {
        // Check if this is our timer event
        if (!IsTimerEvent(LookTimerId, evt))
        {
            return;
        }

        // Move to next direction
        _currentDirectionIndex = (_currentDirectionIndex + 1) % _directions.Length;
        var newDirection = _directions[_currentDirectionIndex];

        // Update facing direction
        SetFacingDirection(newDirection);

        UpdateRandomTimer(LookTimerId, _minInterval, _maxInterval);
    }

    public override void OnUnload()
    {
        CancelTimerIfExists(LookTimerId);
        base.OnUnload();
    }
}
