using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Ecs.Components.Tiles;
using MonoBallFramework.Game.Scripting.Runtime;
using MonoBallFramework.Game.GameSystems.Events;

/// <summary>
///     Impassable east behavior.
///     Blocks movement from east.
/// </summary>
public class ImpassableEastBehavior : ScriptBase
{
    private (int X, int Y) tilePosition;

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx);

        // Cache the tile's position
        if (ctx.Entity.HasValue && ctx.Entity.Value.Has<TilePosition>())
        {
            var pos = ctx.Entity.Value.Get<TilePosition>();
            tilePosition = (pos.X, pos.Y);
        }
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<CollisionCheckEvent>(evt =>
        {
            // Only block if this collision check is for OUR tile
            if (evt.TilePosition == tilePosition && evt.FromDirection == Direction.East)
            {
                evt.PreventDefault("Cannot pass from east");
            }
        });
    }
}

return new ImpassableEastBehavior();
