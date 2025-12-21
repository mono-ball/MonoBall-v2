using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
/// Wander behavior - NPC moves one tile in a random direction, waits, then repeats.
/// Based on proven patrol pattern for reliability.
/// State stored in per-entity WanderState component (not instance fields).
/// </summary>
public class WanderBehavior : ScriptBase
{
    // NO INSTANCE FIELDS! All state in components.

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions

        // Note: Don't access entity components here - no entity attached yet during global init
        // State initialization happens on first tick when entity is available
        ctx.Logger.LogDebug("Wander behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<WanderState>())
            {
                ref var initPos = ref Context.Position;
                Context.World.Add(
                    Context.Entity.Value,
                    new WanderState
                    {
                        WaitTimer = Context.GameState.Random() * 3.0f,
                        MinWaitTime = 1.0f,
                        MaxWaitTime = 4.0f,
                        CurrentDirection = Direction.None,
                        IsMoving = false,
                        MovementCount = 0,
                        StartPosition = new Point(initPos.X, initPos.Y),
                    }
                );
                Context.Logger.LogInformation(
                    "Wander initialized at ({X}, {Y}) | wait: {MinWait}-{MaxWait}s",
                    initPos.X,
                    initPos.Y,
                    1.0f,
                    4.0f
                );
                return; // Skip first tick after initialization
            }

            // Get per-entity state (each NPC has its own)
            ref var state = ref Context.GetState<WanderState>();
            ref var position = ref Context.Position;

            // Wait before next movement
            if (state.WaitTimer > 0)
            {
                state.WaitTimer -= evt.DeltaTime;

                // Deactivate any existing MovementRequest while waiting
                if (Context.World.Has<MovementRequest>(Context.Entity.Value))
                {
                    ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                    request.Active = false;
                }

                return;
            }

            // If we don't have a direction yet, pick one
            if (state.CurrentDirection == Direction.None)
            {
                var directions = new[]
                {
                    Direction.North,
                    Direction.South,
                    Direction.West,
                    Direction.East,
                };
                state.CurrentDirection = directions[
                    Context.GameState.RandomRange(0, directions.Length)
                ];
                state.StartPosition = new Point(position.X, position.Y);
                state.IsMoving = true;
                state.MovingTimer = 0f; // Reset timer when picking new direction
                state.MovementCount++;

                Context.Logger.LogInformation(
                    "Starting wander {Direction} from ({X}, {Y}) - Move #{Count}",
                    state.CurrentDirection,
                    position.X,
                    position.Y,
                    state.MovementCount
                );
            }

            // Track how long we've been trying to move
            if (state.IsMoving)
            {
                state.MovingTimer += evt.DeltaTime;
            }

            // Check if movement completed (reached 1 tile away OR movement system stopped us)
            var gridMovement = Context.World.Get<GridMovement>(Context.Entity.Value);
            var movedOneTitle =
                position.X != state.StartPosition.X || position.Y != state.StartPosition.Y;

            if (state.IsMoving && !gridMovement.IsMoving && movedOneTitle)
            {
                // Successfully moved one tile - start waiting for next move
                Context.Logger.LogInformation(
                    "Wander completed to ({X}, {Y}) | waiting {MinWait}-{MaxWait}s",
                    position.X,
                    position.Y,
                    state.MinWaitTime,
                    state.MaxWaitTime
                );

                // Reset for next move
                state.CurrentDirection = Direction.None;
                state.IsMoving = false;
                state.BlockedAttempts = 0; // Reset blocked counter on successful move
                state.WaitTimer =
                    Context.GameState.Random() * (state.MaxWaitTime - state.MinWaitTime)
                    + state.MinWaitTime;

                // Deactivate movement request
                if (Context.World.Has<MovementRequest>(Context.Entity.Value))
                {
                    ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                    request.Active = false;
                }

                return;
            }

            // If blocked (not moving but didn't reach target), try a new random direction
            // BUT: Only check after giving MovementSystem time to process (0.1 seconds minimum)
            if (
                state.IsMoving
                && !gridMovement.IsMoving
                && !movedOneTitle
                && state.MovingTimer > 0.1f
            )
            {
                state.BlockedAttempts++;

                // After 4 blocked attempts, give up and wait (probably surrounded by obstacles)
                if (state.BlockedAttempts >= 4)
                {
                    Context.Logger.LogInformation(
                        "Wander stuck at ({X}, {Y}) after {Attempts} attempts - waiting",
                        position.X,
                        position.Y,
                        state.BlockedAttempts
                    );

                    // Reset and wait before trying again
                    state.CurrentDirection = Direction.None;
                    state.IsMoving = false;
                    state.BlockedAttempts = 0;
                    state.WaitTimer =
                        Context.GameState.Random() * (state.MaxWaitTime - state.MinWaitTime)
                        + state.MinWaitTime;

                    // Deactivate movement request
                    if (Context.World.Has<MovementRequest>(Context.Entity.Value))
                    {
                        ref var request = ref Context.World.Get<MovementRequest>(
                            Context.Entity.Value
                        );
                        request.Active = false;
                    }

                    return;
                }

                // Pick a NEW random direction immediately (don't wait)
                var directions = new[]
                {
                    Direction.North,
                    Direction.South,
                    Direction.West,
                    Direction.East,
                };
                state.CurrentDirection = directions[
                    Context.GameState.RandomRange(0, directions.Length)
                ];
                state.StartPosition = new Point(position.X, position.Y);
                state.MovingTimer = 0f; // Reset timer for new direction
                // Stay in IsMoving state, no wait timer

                Context.Logger.LogInformation(
                    "Blocked - trying direction {Direction} (attempt {Attempt}/4)",
                    state.CurrentDirection,
                    state.BlockedAttempts
                );

                // Deactivate old movement request (will create new one on next pass)
                if (Context.World.Has<MovementRequest>(Context.Entity.Value))
                {
                    ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                    request.Active = false;
                }

                return;
            }

            // Keep issuing movement request (same pattern as patrol)
            if (state.IsMoving && state.CurrentDirection != Direction.None)
            {
                if (Context.World.Has<MovementRequest>(Context.Entity.Value))
                {
                    ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                    request.Direction = state.CurrentDirection;
                    request.Active = true;
                }
                else
                {
                    Context.World.Add(
                        Context.Entity.Value,
                        new MovementRequest(state.CurrentDirection)
                    );
                }
            }
        });
    }

    public override void OnUnload()
    {
        // Cleanup per-entity state
        if (Context.HasState<WanderState>())
        {
            ref var state = ref Context.GetState<WanderState>();
            Context.Logger.LogInformation(
                "Wander behavior deactivated after {Count} movements",
                state.MovementCount
            );
            Context.RemoveState<WanderState>();
        }

        Context.Logger.LogDebug("Wander behavior deactivated");

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }
}

// Component to store wander-specific state
public struct WanderState
{
    public float WaitTimer;
    public float MinWaitTime;
    public float MaxWaitTime;
    public Direction CurrentDirection;
    public bool IsMoving;
    public int MovementCount;
    public Point StartPosition;
    public int BlockedAttempts; // Track consecutive blocked attempts
    public float MovingTimer; // Time spent trying to move in current direction
}

return new WanderBehavior();
