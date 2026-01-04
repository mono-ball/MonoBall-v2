using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Scripting.Utilities;

/// <summary>
/// Rotate behavior - NPC rotates facing direction clockwise or counter-clockwise periodically.
/// Uses event-driven architecture with TimerElapsedEvent.
/// </summary>
public class RotateBehavior : ScriptBase
{
    private Direction _currentFacing = Direction.South;
    private bool _clockwise = true;
    private const string RotateTimerId = "rotate_timer";
    private float _minInterval = 0.8f;
    private float _maxInterval = 1.5f;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Get parameters
        var rotateInterval = GetParameterAsFloat("rotateInterval", 1.0f);
        _minInterval = rotateInterval * 0.8f; // 80% of base
        _maxInterval = rotateInterval * 1.5f;  // 150% of base
        
        _clockwise = GetParameterAsBool("clockwise", true);
        
        // Get current facing direction
        _currentFacing = TryGetFacingDirection() ?? Direction.South;
        
        // Randomize initial timer to prevent synchronization
        StartRandomTimer(RotateTimerId, _minInterval, _maxInterval, isRepeating: true);
        
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<TimerElapsedEvent>(OnTimerElapsed);
    }

    private void OnTimerElapsed(TimerElapsedEvent evt)
    {
        // Check if this is our rotate timer
        if (!IsTimerEvent(RotateTimerId, evt))
        {
            return;
        }

        // Rotate direction
        _currentFacing = DirectionHelper.Rotate(_currentFacing, _clockwise);
        SetFacingDirection(_currentFacing);
        
        UpdateRandomTimer(RotateTimerId, _minInterval, _maxInterval);
    }

    public override void OnUnload()
    {
        CancelTimerIfExists(RotateTimerId);
        base.OnUnload();
    }
}
