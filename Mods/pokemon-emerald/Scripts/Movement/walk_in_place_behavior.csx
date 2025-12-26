using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Walk In Place behavior - NPC plays walking animation without actually moving.
/// Uses event-driven architecture with MovementStartedEvent to prevent actual movement.
/// </summary>
public class WalkInPlaceBehavior : ScriptBase
{
    private Direction _walkDirection = Direction.South;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Get direction parameter
        _walkDirection = GetParameterAsDirection("direction", Direction.South);
        
        // Get initial facing direction from GridMovement if available, then set walking state
        var currentFacing = TryGetFacingDirection();
        if (currentFacing.HasValue)
        {
            _walkDirection = currentFacing.Value;
        }
        
        SetFacingDirection(_walkDirection);
        Context.Apis.Npc.SetMovementState(Context.Entity.Value, RunningState.Moving);
        
        Context.Logger.Information(
            "Walk in place initialized | direction: {Direction}",
            _walkDirection
        );
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to movement started to cancel any movement attempts
        On<MovementStartedEvent>(OnMovementStarted);
        
        Context.Logger.Debug("Walk in place behavior: Event handlers registered");
    }

    private void OnMovementStarted(ref MovementStartedEvent evt)
    {
        // Only handle events for this entity
        if (!IsEventForThisEntity(ref evt))
        {
            return;
        }

        // Cancel movement - we want to walk in place, not actually move
        evt.IsCancelled = true;
        evt.CancellationReason = "Walk in place behavior - NPC cannot move";
        
        // Ensure facing direction and walking state are maintained
        SetFacingDirection(_walkDirection);
        Context.Apis.Npc.SetMovementState(Context.Entity.Value, RunningState.Moving);
        
        Context.Logger.Debug("Walk in place: Cancelled movement attempt, maintaining walk animation");
    }

    public override void OnUnload()
    {
        // Stop walking animation on unload
        if (Context.Entity.HasValue)
        {
            Context.Apis.Npc.SetMovementState(Context.Entity.Value, RunningState.NotMoving);
        }
        
        Context.Logger.Debug("Walk in place behavior deactivated");
        base.OnUnload();
    }
}
