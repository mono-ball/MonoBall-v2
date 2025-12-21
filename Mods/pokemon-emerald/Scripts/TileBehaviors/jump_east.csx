using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Ecs.Components.Tiles;
using MonoBallFramework.Game.Scripting.Runtime;
using MonoBallFramework.Game.GameSystems.Events;

/// <summary>
///     Jump east behavior.
///     Allows jumping east but blocks west movement.
/// </summary>
public class JumpEastBehavior : ScriptBase
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
        // Block movement from west (can't climb up the ledge)
        On<CollisionCheckEvent>(evt =>
        {
            // Only apply to OUR tile and when moving from west
            if (evt.TilePosition == tilePosition && evt.FromDirection == Direction.West)
            {
                evt.PreventDefault("Can't jump across the ledge from this side");
            }
        });

        // Handle jump effect when moving east onto this tile
        On<MovementCompletedEvent>(
            (evt) =>
            {
                // Check if entity is on our tile after movement
                if (evt.Entity.Has<TilePosition>())
                {
                    var pos = evt.Entity.Get<TilePosition>();
                    if (pos.X == tilePosition.X && pos.Y == tilePosition.Y && evt.Direction == Direction.East)
                    {
                        Context.Logger.LogDebug("Jump east tile: Entity jumped east onto ledge");
                        // Jump animation/effect would go here if API existed
                    }
                }
            }
        );
    }
}

return new JumpEastBehavior();
