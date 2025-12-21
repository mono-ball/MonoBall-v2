using Arch.Core;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
/// Walk In Place behavior - NPC plays walking animation without actually moving.
/// State stored in per-entity WalkInPlaceState component (not instance fields).
/// </summary>
public class WalkInPlaceBehavior : ScriptBase
{
    // NO INSTANCE FIELDS! All state in components.

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions
        ctx.Logger.LogDebug("Walk in place behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<WalkInPlaceState>())
            {
                Context.World.Add(
                    Context.Entity.Value,
                    new WalkInPlaceState
                    {
                        WalkDirection = Direction.South, // Default facing south
                        AnimationTimer = 0f,
                        AnimationInterval = 0.25f, // Animation frame rate
                        IsWalking = true,
                    }
                );

                Context.Logger.LogInformation(
                    "Walk in place initialized | direction: {Direction}",
                    Direction.South
                );
                return; // Skip first tick after initialization
            }

            // Get per-entity state (each NPC has its own)
            ref var state = ref Context.GetState<WalkInPlaceState>();

            // Update facing direction in GridMovement
            if (Context.World.Has<GridMovement>(Context.Entity.Value))
            {
                ref var movement = ref Context.World.Get<GridMovement>(Context.Entity.Value);
                movement.FacingDirection = state.WalkDirection;
                // Force "walking" state to trigger walk animation
                movement.RunningState = RunningState.Moving;
            }

            // Advance animation timer (for frame cycling)
            state.AnimationTimer += evt.DeltaTime;
            if (state.AnimationTimer >= state.AnimationInterval)
            {
                state.AnimationTimer = 0f;
                // Animation system will handle frame cycling
            }
        });
    }

    public override void OnUnload()
    {
        // Cleanup per-entity state
        if (Context.HasState<WalkInPlaceState>())
        {
            Context.RemoveState<WalkInPlaceState>();
        }

        // Stop walking animation on unload
        if (Context.World.Has<GridMovement>(Context.Entity.Value))
        {
            ref var movement = ref Context.World.Get<GridMovement>(Context.Entity.Value);
            movement.RunningState = RunningState.NotMoving;
        }

        Context.Logger.LogDebug("Walk in place behavior deactivated");

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }
}

// Component to store walk-in-place-specific state
public struct WalkInPlaceState
{
    public Direction WalkDirection;
    public float AnimationTimer;
    public float AnimationInterval;
    public bool IsWalking;
}

return new WalkInPlaceBehavior();
