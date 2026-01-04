using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Disguise (Tree) behavior - NPC disguised as a tree (e.g., Sudowoodo).
/// TODO: This is a stub implementation. Requires additional design updates to implement full disguise behavior.
/// </summary>
public class DisguiseTreeBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // TODO: Implement disguise tree behavior event handlers
        // This may include:
        // - MovementStartedEvent to prevent movement while disguised
        // - Interaction events to reveal the disguise (e.g., using Water)
        // - Rendering events to show tree sprite instead of NPC sprite
        // - Player interaction events to trigger reveal
    }

    // TODO: Implement disguise tree behavior logic
    // This may include:
    // - Displaying tree sprite instead of NPC sprite
    // - Preventing movement while disguised
    // - Revealing NPC when player uses specific item (e.g., Water) or interacts
    // - Handling reveal animation
    // - Coordinating with sprite system for disguise sprites
}
