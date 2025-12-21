using Arch.Core;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
/// Stationary behavior - NPC stays in place, does not move.
/// State stored in per-entity StationaryState component (not instance fields).
/// Supports configurable facing direction.
/// </summary>
public class StationaryBehavior : ScriptBase
{
    // NO INSTANCE FIELDS! All state in components.

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions
        ctx.Logger.LogDebug("Stationary behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<StationaryState>())
            {
                // Get initial facing direction from GridMovement if available
                Direction initialDirection = Direction.South;
                if (Context.World.Has<GridMovement>(Context.Entity.Value))
                {
                    initialDirection = Context.World.Get<GridMovement>(Context.Entity.Value).FacingDirection;
                }

                Context.World.Add(
                    Context.Entity.Value,
                    new StationaryState
                    {
                        FacingDirection = initialDirection,
                        Initialized = true,
                    }
                );

                Context.Logger.LogInformation(
                    "Stationary initialized | facing: {Direction}",
                    initialDirection
                );
                return; // Skip first tick after initialization
            }

            // Get per-entity state (each NPC has its own)
            ref var state = ref Context.GetState<StationaryState>();

            // Ensure NPC doesn't move - clear any movement requests
            if (Context.World.Has<MovementRequest>(Context.Entity.Value))
            {
                ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                request.Active = false;
            }

            // Ensure facing direction stays consistent
            if (Context.World.Has<GridMovement>(Context.Entity.Value))
            {
                ref var movement = ref Context.World.Get<GridMovement>(Context.Entity.Value);
                movement.FacingDirection = state.FacingDirection;
                movement.RunningState = RunningState.NotMoving;
            }
        });
    }

    public override void OnUnload()
    {
        // Cleanup per-entity state
        if (Context.HasState<StationaryState>())
        {
            Context.RemoveState<StationaryState>();
        }

        Context.Logger.LogDebug("Stationary behavior deactivated");

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }
}

// Component to store stationary-specific state
public struct StationaryState
{
    public Direction FacingDirection;
    public bool Initialized;
}

return new StationaryBehavior();
