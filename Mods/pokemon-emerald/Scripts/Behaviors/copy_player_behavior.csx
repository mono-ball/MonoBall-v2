using Arch.Core;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Scripting.Runtime;
using PlayerComponent = MonoBallFramework.Game.Ecs.Components.Player.Player;

/// <summary>
/// Copy Player behavior - NPC copies player's facing direction or movement.
/// State stored in per-entity CopyPlayerState component (not instance fields).
/// Supports modes: normal (same direction), opposite (reverse), clockwise/counterclockwise.
/// </summary>
public class CopyPlayerBehavior : ScriptBase
{
    // NO INSTANCE FIELDS! All state in components.

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions
        ctx.Logger.LogDebug("Copy player behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<CopyPlayerState>())
            {
                Context.World.Add(
                    Context.Entity.Value,
                    new CopyPlayerState
                    {
                        CopyMode = CopyMode.Normal, // Default: copy exactly
                        LastPlayerDirection = Direction.South,
                        CurrentFacing = Direction.South,
                    }
                );

                Context.Logger.LogInformation(
                    "Copy player initialized | mode: {Mode}",
                    CopyMode.Normal
                );
                return; // Skip first tick after initialization
            }

            // Get per-entity state (each NPC has its own)
            ref var state = ref Context.GetState<CopyPlayerState>();

            // Find player's facing direction from GridMovement
            Direction playerDirection = Direction.South;
            bool foundPlayer = false;

            // Query for player entity with GridMovement component
            var query = new QueryDescription().WithAll<PlayerComponent, GridMovement>();
            Context.World.Query(
                in query,
                (Entity playerEntity, ref PlayerComponent tag, ref GridMovement playerMovement) =>
                {
                    playerDirection = playerMovement.FacingDirection;
                    foundPlayer = true;
                }
            );

            if (!foundPlayer)
            {
                // No player found, keep current facing
                return;
            }

            // Only update if player direction changed
            if (playerDirection != state.LastPlayerDirection)
            {
                state.LastPlayerDirection = playerDirection;

                // Apply copy mode transformation
                state.CurrentFacing = state.CopyMode switch
                {
                    CopyMode.Normal => playerDirection,
                    CopyMode.Opposite => GetOppositeDirection(playerDirection),
                    CopyMode.Clockwise => RotateClockwise(playerDirection),
                    CopyMode.CounterClockwise => RotateCounterClockwise(playerDirection),
                    _ => playerDirection,
                };

                // Update NPC facing direction in GridMovement
                if (Context.World.Has<GridMovement>(Context.Entity.Value))
                {
                    ref var movement = ref Context.World.Get<GridMovement>(Context.Entity.Value);
                    movement.FacingDirection = state.CurrentFacing;
                }

                Context.Logger.LogDebug(
                    "Copied player direction {PlayerDir} -> {NpcDir} (mode: {Mode})",
                    playerDirection,
                    state.CurrentFacing,
                    state.CopyMode
                );
            }
        });
    }

    private static Direction GetOppositeDirection(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.South,
            Direction.South => Direction.North,
            Direction.East => Direction.West,
            Direction.West => Direction.East,
            _ => dir,
        };
    }

    private static Direction RotateClockwise(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.East,
            Direction.East => Direction.South,
            Direction.South => Direction.West,
            Direction.West => Direction.North,
            _ => dir,
        };
    }

    private static Direction RotateCounterClockwise(Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.West,
            Direction.West => Direction.South,
            Direction.South => Direction.East,
            Direction.East => Direction.North,
            _ => dir,
        };
    }

    public override void OnUnload()
    {
        // Cleanup per-entity state
        if (Context.HasState<CopyPlayerState>())
        {
            Context.RemoveState<CopyPlayerState>();
        }

        Context.Logger.LogDebug("Copy player behavior deactivated");

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }
}

// Copy mode enum for different copying styles
public enum CopyMode
{
    Normal,         // Same direction as player
    Opposite,       // Opposite direction (face player)
    Clockwise,      // 90 degrees clockwise from player
    CounterClockwise // 90 degrees counter-clockwise from player
}

// Component to store copy-player-specific state
public struct CopyPlayerState
{
    public CopyMode CopyMode;
    public Direction LastPlayerDirection;
    public Direction CurrentFacing;
}

return new CopyPlayerBehavior();
