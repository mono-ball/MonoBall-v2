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
        
        Context.Logger.Information(
            "Rotate initialized | clockwise: {Clockwise}, interval: {MinInterval}-{MaxInterval}s",
            _clockwise,
            _minInterval,
            _maxInterval
        );
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to timer elapsed to know when to rotate
        On<TimerElapsedEvent>(OnTimerElapsed);
        
        Context.Logger.Debug("Rotate behavior: Event handlers registered");
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
        
        // Update facing direction
        SetFacingDirection(_currentFacing);
        
        // Set new random interval for next rotation
        // Note: For repeating timers, we need to cancel and restart with new interval
        // since repeating timers use the same duration each cycle
        CancelTimer(RotateTimerId);
        StartRandomTimer(RotateTimerId, _minInterval, _maxInterval, isRepeating: true);
        
        Context.Logger.Debug(
            "Rotated to {Direction} ({RotationType})",
            _currentFacing,
            _clockwise ? "clockwise" : "counter-clockwise"
        );
    }

    public override void OnUnload()
    {
        // Cancel any active timers
        CancelTimerIfExists(RotateTimerId);
        
        Context.Logger.Debug("Rotate behavior deactivated");
        base.OnUnload();
    }
}
