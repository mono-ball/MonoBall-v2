using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Follow behavior - NPC follows the player or copies player movement.
/// TODO: This is a stub implementation. Requires additional design updates to implement full follow behavior.
/// </summary>
public class FollowBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // TODO: Implement follow behavior event handlers
        // This may include:
        // - MovementCompletedEvent to track player movement
        // - Player movement events to trigger following
        // - Timer events for follow delay/spacing
        // - Distance calculation events
    }

    // TODO: Implement follow behavior logic
    // This may include:
    // - Tracking player position and movement
    // - Moving NPC to follow player at appropriate distance
    // - Handling follow spacing/delay
    // - Coordinating with movement system for smooth following
    // - Handling obstacles and pathfinding
}
