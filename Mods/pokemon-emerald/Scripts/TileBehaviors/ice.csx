using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Scripting.Runtime;
using MonoBallFramework.Game.GameSystems.Events;

/// <summary>
///     Ice tile behavior.
///     Forces sliding movement in the current direction.
/// </summary>
public class IceBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        // Handle sliding when movement completes on ice
        On<MovementCompletedEvent>(
            (evt) =>
            {
                Context.Logger.LogDebug(
                    $"Ice tile: Movement completed with direction {evt.Direction}"
                );

                // Continue sliding in current direction if valid
                if (evt.Direction != Direction.None)
                {
                    Context.Logger.LogDebug(
                        $"Ice tile: Forcing continued movement in direction {evt.Direction}"
                    );
                    // The movement system will pick up the forced direction from tile metadata
                }
            }
        );
    }
}

return new IceBehavior();
