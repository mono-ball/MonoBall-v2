using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Disguise (Rock) behavior - NPC disguised as a rock or mountain (e.g., Kecleon).
/// TODO: This is a stub implementation. Requires additional design updates to implement full disguise behavior.
/// </summary>
public class DisguiseRockBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // TODO: Implement disguise rock behavior event handlers
        // This may include:
        // - MovementStartedEvent to prevent movement while disguised
        // - Interaction events to reveal the disguise
        // - Rendering events to show rock sprite instead of NPC sprite
        // - Player proximity events to trigger reveal
    }

    // TODO: Implement disguise rock behavior logic
    // This may include:
    // - Displaying rock/mountain sprite instead of NPC sprite
    // - Preventing movement while disguised
    // - Revealing NPC when player interacts or gets close
    // - Handling reveal animation
    // - Coordinating with sprite system for disguise sprites
}
