using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Hidden/Invisible behavior - NPC is hidden and does not move.
/// Uses event-driven architecture with MovementStartedEvent to prevent movement.
/// Hides entity by setting RenderableComponent.IsVisible = false.
/// </summary>
public class HiddenBehavior : ScriptBase
{
    private bool _wasVisible = false;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        if (!Context.Entity.HasValue)
        {
            return;
        }
        
        // Hide the entity by setting IsVisible = false
        if (Context.HasComponent<RenderableComponent>())
        {
            var renderable = Context.GetComponent<RenderableComponent>();
            _wasVisible = renderable.IsVisible;
            renderable.IsVisible = false;
            Context.SetComponent(renderable);
        }
        
        Context.Logger.Information("Hidden behavior initialized | NPC hidden");
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to movement started to cancel any movement attempts
        On<MovementStartedEvent>(OnMovementStarted);
        
        Context.Logger.Debug("Hidden behavior: Event handlers registered");
    }

    private void OnMovementStarted(ref MovementStartedEvent evt)
    {
        // Only handle events for this entity
        if (!IsEventForThisEntity(ref evt))
        {
            return;
        }

        // Cancel movement - hidden NPCs cannot move
        evt.IsCancelled = true;
        evt.CancellationReason = "Hidden behavior - NPC cannot move";
        
        // Ensure NPC doesn't move
        if (Context.Entity.HasValue)
        {
            Context.Apis.Npc.SetMovementState(Context.Entity.Value, RunningState.NotMoving);
        }
    }

    public override void OnUnload()
    {
        // Restore visibility if we hid it
        if (Context.Entity.HasValue && Context.HasComponent<RenderableComponent>())
        {
            var renderable = Context.GetComponent<RenderableComponent>();
            renderable.IsVisible = _wasVisible;
            Context.SetComponent(renderable);
        }
        
        Context.Logger.Debug("Hidden behavior deactivated");
        base.OnUnload();
    }
}
