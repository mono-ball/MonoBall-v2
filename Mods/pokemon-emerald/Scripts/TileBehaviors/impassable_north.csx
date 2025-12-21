using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Ecs.Components.Tiles;
using MonoBallFramework.Game.Scripting.Runtime;
using MonoBallFramework.Game.GameSystems.Events;

/// <summary>
///     Impassable north behavior.
///     Blocks movement from north.
/// </summary>
public class ImpassableNorthBehavior : ScriptBase
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
            if (evt.TilePosition == tilePosition && evt.FromDirection == Direction.North)
            {
                evt.PreventDefault("Cannot pass from north");
            }
        });
    }
}

return new ImpassableNorthBehavior();
