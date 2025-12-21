using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
///     Normal walkable tile behavior.
///     Default behavior that allows all movement.
/// </summary>
public class NormalBehavior : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        // Walkable tile, no restrictions
    }
}

return new NormalBehavior();
