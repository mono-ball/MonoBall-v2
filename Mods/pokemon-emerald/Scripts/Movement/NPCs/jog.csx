using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;

/// <summary>
/// Jog behavior - NPC jogs or runs in place or pattern.
/// TODO: This is a stub implementation. Requires additional design updates to implement full jog behavior.
/// </summary>
public class JogBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // TODO: Implement jog behavior event handlers
        // This may include:
        // - MovementStartedEvent to handle jog initiation
        // - MovementCompletedEvent to handle jog completion
        // - TimerElapsedEvent for jog timing/patterns
        // - Other events as needed for jog patterns
    }

    // TODO: Implement jog behavior logic
    // This may include:
    // - Jogging in a specific direction or pattern
    // - Jogging in place (animation only)
    // - Handling jog animations and state
    // - Coordinating with movement system
    // - Differentiating jog speed from walk speed
}
