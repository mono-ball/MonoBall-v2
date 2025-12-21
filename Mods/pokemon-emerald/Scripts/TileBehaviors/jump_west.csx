using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Ecs.Components.Tiles;
using MonoBallFramework.Game.Scripting.Runtime;
using MonoBallFramework.Game.GameSystems.Events;

/// <summary>
///     Jump west behavior.
///     Allows jumping west but blocks east movement.
/// </summary>
public class JumpWestBehavior : ScriptBase
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
        // Block movement from east (can't climb up the ledge)
        On<CollisionCheckEvent>(evt =>
        {
            // Only apply to OUR tile and when moving from east
            if (evt.TilePosition == tilePosition && evt.FromDirection == Direction.East)
            {
                evt.PreventDefault("Can't jump across the ledge from this side");
            }
        });

        // Handle jump effect when moving west onto this tile
        On<MovementCompletedEvent>(
            (evt) =>
            {
                // Check if entity is on our tile after movement
                if (evt.Entity.Has<TilePosition>())
                {
                    var pos = evt.Entity.Get<TilePosition>();
                    if (pos.X == tilePosition.X && pos.Y == tilePosition.Y && evt.Direction == Direction.West)
                    {
                        Context.Logger.LogDebug("Jump west tile: Entity jumped west onto ledge");
                        // Jump animation/effect would go here if API existed
                    }
                }
            }
        );
    }
}

return new JumpWestBehavior();
