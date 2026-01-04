using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Stationary behavior - NPC stays in place, does not move.
/// </summary>
public class StationaryBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<MovementStartedEvent>(OnMovementStarted);
    }

    private void OnMovementStarted(ref MovementStartedEvent evt)
    {
        if (!IsEventForThisEntity(ref evt))
            return;

        evt.IsCancelled = true;
        evt.CancellationReason = "Stationary behavior - NPC cannot move";
    }
}
