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
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<MovementStartedEvent>(OnMovementStarted);
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
        
        SetFacingDirection(_walkDirection);
        Context.Apis.Npc.SetMovementState(Context.Entity.Value, RunningState.Moving);
    }

    public override void OnUnload()
    {
        if (Context.Entity.HasValue)
            Context.Apis.Npc.SetMovementState(Context.Entity.Value, RunningState.NotMoving);
        base.OnUnload();
    }
}
