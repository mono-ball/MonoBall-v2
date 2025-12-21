using MonoBallFramework.Game.Scripting.Runtime;
using MonoBallFramework.Game.GameSystems.Events;
using MonoBallFramework.Game.Ecs.Components.Tiles;

/// <summary>
///     Impassable tile behavior.
///     Blocks all movement in any direction.
/// </summary>
public class ImpassableBehavior : ScriptBase
{
    private (int X, int Y) tilePosition;

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx);

        // Cache the tile's position so we can check if events are for this tile
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
            if (evt.TilePosition == tilePosition)
            {
                evt.PreventDefault("Tile is impassable");
            }
        });
    }
}

return new ImpassableBehavior();
