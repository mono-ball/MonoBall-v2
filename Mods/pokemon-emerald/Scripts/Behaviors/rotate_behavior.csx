using Arch.Core;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
/// Rotate behavior - NPC rotates facing direction clockwise or counter-clockwise periodically.
/// State stored in per-entity RotateState component (not instance fields).
/// </summary>
public class RotateBehavior : ScriptBase
{
    // NO INSTANCE FIELDS! All state in components.

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions
        ctx.Logger.LogDebug("Rotate behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<RotateState>())
            {
                // Use entity position to seed randomization (prevents synchronization)
                ref var position = ref Context.Position;
                var entitySeed = (position.X * 73856093) ^ (position.Y * 19349663) ^ (int)evt.TotalTime;
                var entityRandom = new System.Random(entitySeed);
                
                // Randomize initial timer to prevent synchronization (Pokemon Emerald pattern)
                var initialDelay = (float)entityRandom.NextDouble() * 0.8f + 0.2f; // 0.2-1.0 seconds
                
                // Get current facing from GridMovement (already set by map)
                Direction currentFacing = Direction.South;
                if (Context.World.Has<GridMovement>(Context.Entity.Value))
                {
                    currentFacing = Context.World.Get<GridMovement>(Context.Entity.Value).FacingDirection;
                }
                
                Context.World.Add(
                    Context.Entity.Value,
                    new RotateState
                    {
                        RotateTimer = initialDelay,
                        RotateInterval = 1.0f,  // Base interval
                        MinInterval = 0.8f,     // Minimum random interval
                        MaxInterval = 1.5f,     // Maximum random interval
                        Clockwise = true,
                        CurrentFacing = currentFacing,
                        RandomSeed = entitySeed, // Store seed for consistent randomization
                    }
                );

                Context.Logger.LogInformation(
                    "Rotate initialized | clockwise: {Clockwise}, interval: {MinInterval}-{MaxInterval}s",
                    true,
                    0.8f,
                    1.5f
                );
                return; // Skip first tick after initialization
            }

            // Get per-entity state (each NPC has its own)
            ref var state = ref Context.GetState<RotateState>();

            // Count down timer
            state.RotateTimer -= evt.DeltaTime;

            if (state.RotateTimer <= 0)
            {
                // Time to rotate
                state.CurrentFacing = RotateDirection(state.CurrentFacing, state.Clockwise);
                
                // Use entity-specific random seed for consistent but unique randomization
                var entityRandom = new System.Random(state.RandomSeed + (int)(evt.TotalTime * 1000));
                state.RotateTimer = (float)entityRandom.NextDouble() * (state.MaxInterval - state.MinInterval) + state.MinInterval;

                // Update facing direction in GridMovement component
                if (Context.World.Has<GridMovement>(Context.Entity.Value))
                {
                    ref var movement = ref Context.World.Get<GridMovement>(Context.Entity.Value);
                    movement.FacingDirection = state.CurrentFacing;
                }

                Context.Logger.LogDebug(
                    "Rotated to {Direction} ({RotationType})",
                    state.CurrentFacing,
                    state.Clockwise ? "clockwise" : "counter-clockwise"
                );
            }
        });
    }

    private static Direction RotateDirection(Direction current, bool clockwise)
    {
        if (clockwise)
        {
            return current switch
            {
                Direction.North => Direction.East,
                Direction.East => Direction.South,
                Direction.South => Direction.West,
                Direction.West => Direction.North,
                _ => Direction.South,
            };
        }
        else
        {
            // Counter-clockwise
            return current switch
            {
                Direction.North => Direction.West,
                Direction.West => Direction.South,
                Direction.South => Direction.East,
                Direction.East => Direction.North,
                _ => Direction.South,
            };
        }
    }

    public override void OnUnload()
    {
        // Cleanup per-entity state
        if (Context.HasState<RotateState>())
        {
            Context.RemoveState<RotateState>();
        }

        Context.Logger.LogDebug("Rotate behavior deactivated");

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }
}

// Component to store rotate-specific state
public struct RotateState
{
    public float RotateTimer;
    public float RotateInterval;  // Base interval (for backwards compatibility)
    public float MinInterval;      // Minimum random interval
    public float MaxInterval;      // Maximum random interval
    public bool Clockwise;
    public Direction CurrentFacing;
    public int RandomSeed;         // Entity-specific seed for consistent randomization
}

return new RotateBehavior();
