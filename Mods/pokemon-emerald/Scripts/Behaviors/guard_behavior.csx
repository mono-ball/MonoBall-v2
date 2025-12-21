using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
/// Guard behavior - NPC stays at position, scans for threats.
/// Uses GuardState component for per-entity state.
/// </summary>
public class GuardBehavior : ScriptBase
{
    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions

        // Note: Don't access entity components here - no entity attached yet during global init
        // State initialization happens on first tick when entity is available
        ctx.Logger.LogDebug("Guard behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<GuardState>())
            {
                ref var initPos = ref Context.Position;

                // Use entity position to seed randomization (prevents synchronization)
                var entitySeed = (initPos.X * 73856093) ^ (initPos.Y * 19349663) ^ (int)evt.TotalTime;
                var entityRandom = new System.Random(entitySeed);
                
                // Randomize initial scan timer to prevent synchronization (Pokemon Emerald pattern)
                var initialDelay = (float)entityRandom.NextDouble() * 1.5f + 0.5f; // 0.5-2.0 seconds
                
                // Get current facing from GridMovement (already set by map)
                Direction currentFacing = Direction.South;
                if (Context.World.Has<GridMovement>(Context.Entity.Value))
                {
                    currentFacing = Context.World.Get<GridMovement>(Context.Entity.Value).FacingDirection;
                }
                
                Context.World.Add(
                    Context.Entity.Value,
                    new GuardState
                    {
                        GuardPosition = new Point(initPos.X, initPos.Y),
                        FacingDirection = currentFacing,
                        ScanTimer = initialDelay,
                        ScanInterval = 2.0f,  // Base interval
                        MinInterval = 1.5f,    // Minimum random interval
                        MaxInterval = 3.0f,    // Maximum random interval
                        RandomSeed = entitySeed, // Store seed for consistent randomization
                    }
                );

                Context.Logger.LogInformation(
                    "Guard activated at position ({X}, {Y})",
                    initPos.X,
                    initPos.Y
                );
                return; // Skip first tick after initialization
            }

            ref var state = ref Context.GetState<GuardState>();
            ref var position = ref Context.Position;

            // Return to guard position if moved
            if (position.X != state.GuardPosition.X || position.Y != state.GuardPosition.Y)
            {
                var dir = Context.Map.GetDirectionTo(
                    position.X,
                    position.Y,
                    state.GuardPosition.X,
                    state.GuardPosition.Y
                );

                // Use component pooling: reuse existing component or add new one
                if (Context.World.Has<MovementRequest>(Context.Entity.Value))
                {
                    ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                    request.Direction = dir;
                    request.Active = true;
                }
                else
                {
                    Context.World.Add(Context.Entity.Value, new MovementRequest(dir));
                }

                Context.Logger.LogDebug(
                    "Guard returning to post from ({X}, {Y}) to ({GuardX}, {GuardY})",
                    position.X,
                    position.Y,
                    state.GuardPosition.X,
                    state.GuardPosition.Y
                );
                return;
            }

            // Rotate scan direction periodically
            state.ScanTimer -= evt.DeltaTime;
            if (state.ScanTimer <= 0)
            {
                state.FacingDirection = RotateDirection(state.FacingDirection);
                
                // Use entity-specific random seed for consistent but unique randomization
                var entityRandom = new System.Random(state.RandomSeed + (int)(evt.TotalTime * 1000));
                state.ScanTimer = (float)entityRandom.NextDouble() * (state.MaxInterval - state.MinInterval) + state.MinInterval;

                // Update NPC facing direction (if we had that component)
                Context.Logger.LogTrace("Guard facing {Direction}", state.FacingDirection);
            }
        });
    }

    public override void OnUnload()
    {
        Context.Logger.LogInformation("Guard deactivated");
        // Remove guard state component when script is deactivated
        if (Context.World.Has<GuardState>(Context.Entity.Value))
        {
            Context.World.Remove<GuardState>(Context.Entity.Value);
        }

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }

    private static Direction RotateDirection(Direction current)
    {
        return current switch
        {
            Direction.North => Direction.East,
            Direction.East => Direction.South,
            Direction.South => Direction.West,
            Direction.West => Direction.North,
            _ => Direction.North,
        };
    }
}

// Component to store guard-specific state
public struct GuardState
{
    public Point GuardPosition;
    public Direction FacingDirection;
    public float ScanTimer;
    public float ScanInterval;  // Base interval (for backwards compatibility)
    public float MinInterval;    // Minimum random interval
    public float MaxInterval;    // Maximum random interval
    public int RandomSeed;       // Entity-specific seed for consistent randomization
}

return new GuardBehavior();
