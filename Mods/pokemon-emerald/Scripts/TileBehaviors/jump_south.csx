using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Ecs.Components.Tiles;
using MonoBallFramework.Game.Scripting.Runtime;
using MonoBallFramework.Game.GameSystems.Events;

/// <summary>
///     Jump south behavior.
///     Allows jumping south but blocks north movement.
/// </summary>
public class JumpSouthBehavior : ScriptBase
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
        // Block movement from north (can't climb up the ledge)
        On<CollisionCheckEvent>(evt =>
        {
            // Only apply to OUR tile and when moving from north
            if (evt.TilePosition == tilePosition && evt.FromDirection == Direction.North)
            {
                evt.PreventDefault("Can't climb up the ledge");
            }
        });

        // Handle jump effect when moving south onto this tile
        On<MovementCompletedEvent>(
            (evt) =>
            {
                // Check if entity is on our tile after movement
                if (evt.Entity.Has<TilePosition>())
                {
                    var pos = evt.Entity.Get<TilePosition>();
                    if (pos.X == tilePosition.X && pos.Y == tilePosition.Y && evt.Direction == Direction.South)
                    {
                        Context.Logger.LogDebug("Jump south tile: Entity jumped south onto ledge");
                        // Jump animation/effect would go here if API existed
                    }
                }
            }
        );
    }
}

return new JumpSouthBehavior();
