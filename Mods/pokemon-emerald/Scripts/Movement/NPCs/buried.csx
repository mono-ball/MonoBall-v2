using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Buried behavior - NPC is hidden underground (e.g., Diglett encounters).
/// TODO: This is a stub implementation. Requires additional design updates to implement full buried behavior.
/// </summary>
public class BuriedBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // TODO: Implement buried behavior event handlers
        // This may include:
        // - MovementStartedEvent to prevent movement while buried
        // - Rendering events to show/hide underground sprite
        // - Interaction events for encounters (e.g., Diglett popping up)
        // - Timer events for periodic appearance/disappearance
    }

    // TODO: Implement buried behavior logic
    // This may include:
    // - Hiding the entity underground (visual state)
    // - Preventing movement while buried
    // - Handling encounters when player steps on buried NPC
    // - Animation for appearing/disappearing
    // - Coordinating with rendering system for underground sprites
}
