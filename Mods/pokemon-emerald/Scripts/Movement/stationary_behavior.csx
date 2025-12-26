using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Stationary behavior - NPC stays in place, does not move.
/// Ensures NPC doesn't move and maintains facing direction.
/// </summary>
public class StationaryBehavior : ScriptBase
{
    private Direction _facingDirection = Direction.South;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Load persisted facing direction
        _facingDirection = GetDirection("facingDirection", Direction.South);
        
        // Get initial facing direction from GridMovement if available
        var currentFacing = TryGetFacingDirection();
        if (currentFacing.HasValue)
        {
            _facingDirection = currentFacing.Value;
        }
        
        Context.Logger.Information(
            "Stationary behavior initialized. Facing: {Direction}",
            _facingDirection
        );
        
        // Ensure NPC doesn't move
        EnsureStationary();
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to movement started to cancel any movement attempts
        On<MovementStartedEvent>(OnMovementStarted);
        
        Context.Logger.Debug("Stationary behavior: Event handlers registered");
    }

    private void OnMovementStarted(ref MovementStartedEvent evt)
    {
        // Only handle events for this entity
        if (!IsEventForThisEntity(ref evt))
        {
            return;
        }

        // Cancel movement
        evt.IsCancelled = true;
        evt.CancellationReason = "Stationary behavior - NPC cannot move";
        
        Context.Logger.Debug("Stationary behavior: Cancelled movement attempt");
    }

    public override void OnUnload()
    {
        SaveState();
        Context.Logger.Debug("Stationary behavior unloaded");
        base.OnUnload();
    }
    
    private void EnsureStationary()
    {
        if (!Context.Entity.HasValue)
        {
            return;
        }
        
        // Clear any movement requests
        // Note: MovementRequest is a component, we can't easily remove it
        // Instead, we'll cancel movement via MovementStartedEvent
        
        // Ensure facing direction stays consistent
        SetFacingDirection(_facingDirection);
        Context.Apis.Npc.SetMovementState(Context.Entity.Value, RunningState.NotMoving);
    }

    private void SaveState()
    {
        SetDirection("facingDirection", _facingDirection);
    }
}
