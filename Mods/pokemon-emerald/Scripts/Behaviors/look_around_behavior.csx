using Arch.Core;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
/// Look Around behavior - NPC periodically changes facing direction.
/// State stored in per-entity LookAroundState component (not instance fields).
/// </summary>
public class LookAroundBehavior : ScriptBase
{
    // NO INSTANCE FIELDS! All state in components.

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions
        ctx.Logger.LogDebug("Look around behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<LookAroundState>())
            {
                // Default directions: all 4 cardinals
                var directions = new[] { Direction.North, Direction.South, Direction.East, Direction.West };
                
                // Use entity position to seed randomization (prevents synchronization)
                // This ensures each NPC gets unique random values even if initialized in same frame
                ref var position = ref Context.Position;
                var entitySeed = (position.X * 73856093) ^ (position.Y * 19349663) ^ (int)evt.TotalTime;
                var entityRandom = new System.Random(entitySeed);
                
                // Randomize initial timer to prevent synchronization (Pokemon Emerald pattern)
                // Use random value between 0.5 and 2.0 seconds for initial delay
                var initialDelay = (float)entityRandom.NextDouble() * 1.5f + 0.5f;
                
                // Get current facing from GridMovement (already set by map)
                Direction currentFacing = Direction.South;
                if (Context.World.Has<GridMovement>(Context.Entity.Value))
                {
                    currentFacing = Context.World.Get<GridMovement>(Context.Entity.Value).FacingDirection;
                }
                
                // Find initial direction index based on current facing
                int initialIndex = 0;
                for (int i = 0; i < directions.Length; i++)
                {
                    if (directions[i] == currentFacing)
                    {
                        initialIndex = i;
                        break;
                    }
                }
                
                Context.World.Add(
                    Context.Entity.Value,
                    new LookAroundState
                    {
                        LookTimer = initialDelay,
                        LookInterval = 2.0f, // Base interval
                        MinInterval = 1.5f,  // Minimum random interval
                        MaxInterval = 3.0f,  // Maximum random interval
                        CurrentDirectionIndex = initialIndex,
                        Directions = directions,
                        CurrentFacing = currentFacing,
                        RandomSeed = entitySeed, // Store seed for consistent randomization
                    }
                );

                Context.Logger.LogInformation(
                    "Look around initialized | directions: {Count}, interval: {MinInterval}-{MaxInterval}s",
                    directions.Length,
                    1.5f,
                    3.0f
                );
                return; // Skip first tick after initialization
            }

            // Get per-entity state (each NPC has its own)
            ref var state = ref Context.GetState<LookAroundState>();

            // Count down timer
            state.LookTimer -= evt.DeltaTime;

            if (state.LookTimer <= 0)
            {
                // Time to look in a new direction
                state.CurrentDirectionIndex = (state.CurrentDirectionIndex + 1) % state.Directions.Length;
                state.CurrentFacing = state.Directions[state.CurrentDirectionIndex];
                
                // Use entity-specific random seed for consistent but unique randomization
                var entityRandom = new System.Random(state.RandomSeed + (int)(evt.TotalTime * 1000));
                state.LookTimer = (float)entityRandom.NextDouble() * (state.MaxInterval - state.MinInterval) + state.MinInterval;

                // Update facing direction in GridMovement component
                if (Context.World.Has<GridMovement>(Context.Entity.Value))
                {
                    ref var movement = ref Context.World.Get<GridMovement>(Context.Entity.Value);
                    movement.FacingDirection = state.CurrentFacing;
                }

                Context.Logger.LogDebug(
                    "Looking {Direction} (index {Index}/{Total})",
                    state.CurrentFacing,
                    state.CurrentDirectionIndex,
                    state.Directions.Length
                );
            }
        });
    }

    public override void OnUnload()
    {
        // Cleanup per-entity state
        if (Context.HasState<LookAroundState>())
        {
            Context.RemoveState<LookAroundState>();
        }

        Context.Logger.LogDebug("Look around behavior deactivated");

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }
}

// Component to store look-around-specific state
public struct LookAroundState
{
    public float LookTimer;
    public float LookInterval;  // Base interval (for backwards compatibility)
    public float MinInterval;    // Minimum random interval
    public float MaxInterval;    // Maximum random interval
    public int CurrentDirectionIndex;
    public Direction[] Directions;
    public Direction CurrentFacing;
    public int RandomSeed;       // Entity-specific seed for consistent randomization
}

return new LookAroundBehavior();
