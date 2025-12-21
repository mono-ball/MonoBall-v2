using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Ecs.Components.Tiles;
using MonoBallFramework.Game.Scripting.Runtime;
using MonoBallFramework.Game.GameSystems.Events;

/// <summary>
///     Jump north behavior.
///     Allows jumping north but blocks south movement.
/// </summary>
public class JumpNorthBehavior : ScriptBase
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
        // Block movement from south (can't climb up the ledge)
        On<CollisionCheckEvent>(evt =>
        {
            // Only apply to OUR tile and when moving from south
            if (evt.TilePosition == tilePosition && evt.FromDirection == Direction.South)
            {
                evt.PreventDefault("Can't climb up the ledge");
            }
        });

        // Handle jump effect when moving north onto this tile
        On<MovementCompletedEvent>(
            (evt) =>
            {
                // Check if entity is on our tile after movement
                if (evt.Entity.Has<TilePosition>())
                {
                    var pos = evt.Entity.Get<TilePosition>();
                    if (pos.X == tilePosition.X && pos.Y == tilePosition.Y && evt.Direction == Direction.North)
                    {
                        Context.Logger.LogDebug("Jump north tile: Entity jumped north onto ledge");
                        // Jump animation/effect would go here if API existed
                    }
                }
            }
        );
    }
}

return new JumpNorthBehavior();
