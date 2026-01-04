using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Invisible behavior - NPC is invisible and does not interact.
/// TODO: This is a stub implementation. Requires additional design updates to implement full invisible behavior.
/// </summary>
public class InvisibleBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // TODO: Implement invisible behavior event handlers
        // This may include:
        // - MovementStartedEvent to prevent movement
        // - Rendering events to hide the entity
        // - Interaction events to prevent interactions
        // - Visibility toggle events
    }

    // TODO: Implement invisible behavior logic
    // This may include:
    // - Hiding the entity (setting IsVisible = false)
    // - Preventing movement while invisible
    // - Preventing interactions while invisible
    // - Coordinating with rendering system
    // - Note: Similar to HiddenBehavior but may have different use cases
}
