using System.Linq;
using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Ecs.Components.NPCs;
using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
/// Patrol behavior using ScriptContext pattern.
/// State stored in per-entity PatrolState component (not instance fields).
/// </summary>
public class PatrolBehavior : ScriptBase
{
    // NO INSTANCE FIELDS! All state in components.

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions

        // Note: Don't access entity components here - no entity attached yet during global init
        // State initialization happens on first tick when entity is available
        ctx.Logger.LogDebug("Patrol behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<PatrolState>())
            {
                // PatrolBehavior REQUIRES MovementRoute component with waypoints
                // If missing, the NPC map object doesn't have 'waypoints' property defined
                if (!Context.World.Has<MovementRoute>(Context.Entity.Value))
                {
                    Context.Logger.LogWarning(
                        "PatrolBehavior requires MovementRoute component. Add 'waypoints' property to map object. Deactivating behavior.");

                    // Add empty state to prevent repeated initialization attempts
                    Context.World.Add(
                        Context.Entity.Value,
                        new PatrolState
                        {
                            CurrentWaypoint = -1, // Mark as invalid
                            WaitTimer = 0f,
                            WaitDuration = 0f,
                            Speed = 0f,
                            IsWaiting = true, // Stay idle
                            RandomSeed = 0, // Not used in invalid state
                        }
                    );
                    return;
                }

                ref var initPath = ref Context.World.Get<MovementRoute>(Context.Entity.Value);

                // Log all waypoints for debugging
                var waypointStr = string.Join(
                    ", ",
                    initPath.Waypoints.Select(p => $"({p.X},{p.Y})")
                );
                Context.Logger.LogInformation(
                    "Patrol initialized | waypoints: {Count}, loop: {Loop}, path: {Path}",
                    initPath.Waypoints.Length,
                    initPath.Loop,
                    waypointStr
                );

                // Use entity position to seed randomization (prevents synchronization)
                ref var position = ref Context.Position;
                var entitySeed = (position.X * 73856093) ^ (position.Y * 19349663) ^ (int)evt.TotalTime;
                var entityRandom = new System.Random(entitySeed);
                
                // Randomize initial wait timer to prevent synchronization (Pokemon Emerald pattern)
                // If waypoint wait time is > 0, randomize initial delay; otherwise start immediately
                var initialWait = initPath.WaypointWaitTime > 0 
                    ? (float)entityRandom.NextDouble() * initPath.WaypointWaitTime 
                    : 0f;
                
                Context.World.Add(
                    Context.Entity.Value,
                    new PatrolState
                    {
                        CurrentWaypoint = 0,
                        WaitTimer = initialWait,
                        WaitDuration = initPath.WaypointWaitTime,
                        Speed = 4.0f,
                        IsWaiting = initialWait > 0,
                        RandomSeed = entitySeed, // Store seed for consistent randomization
                    }
                );
                return; // Skip first tick after initialization
            }

            // Get per-entity state (each NPC has its own)
            ref var state = ref Context.GetState<PatrolState>();

            // Skip if behavior is in invalid state (missing MovementRoute)
            if (state.CurrentWaypoint < 0)
            {
                return;
            }

            ref var path = ref Context.World.Get<MovementRoute>(Context.Entity.Value);
            ref var position = ref Context.Position;

            // Check path validity
            if (path.Waypoints == null || path.Waypoints.Length == 0)
            {
                Context.Logger.LogWarning("Path has no waypoints");
                return;
            }

            // Wait at waypoint
            if (state.WaitTimer > 0)
            {
                state.WaitTimer -= evt.DeltaTime;
                state.IsWaiting = true;
                return;
            }

            state.IsWaiting = false;

            var target = path.Waypoints[state.CurrentWaypoint];

            // Reached waypoint? Check BOTH grid position AND movement completion
            var isMoving = Context.World.Get<GridMovement>(Context.Entity.Value).IsMoving;
            if (position.X == target.X && position.Y == target.Y && !isMoving)
            {
                Context.Logger.LogInformation(
                    "Reached waypoint {Index}/{Total}: ({X},{Y}) | wait={Wait}s",
                    state.CurrentWaypoint,
                    path.Waypoints.Length - 1,
                    target.X,
                    target.Y,
                    state.WaitDuration
                );

                state.CurrentWaypoint++;
                if (state.CurrentWaypoint >= path.Waypoints.Length)
                {
                    Context.Logger.LogInformation(
                        "End of path reached, Loop={Loop}, resetting to {Index}",
                        path.Loop,
                        path.Loop ? 0 : path.Waypoints.Length - 1
                    );
                    state.CurrentWaypoint = path.Loop ? 0 : path.Waypoints.Length - 1;
                }

                // Randomize wait time to prevent synchronization (Pokemon Emerald pattern)
                // Add ±20% variation to wait duration
                var variation = state.WaitDuration * 0.2f;
                var entityRandom = new System.Random(state.RandomSeed + (int)(evt.TotalTime * 1000));
                state.WaitTimer = (float)entityRandom.NextDouble() * (variation * 2) + (state.WaitDuration - variation);

                // Deactivate any existing MovementRequest when reaching waypoint
                if (Context.World.Has<MovementRequest>(Context.Entity.Value))
                {
                    ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                    request.Active = false;
                }

                return;
            }

            // Move toward waypoint
            var direction = Context.Map.GetDirectionTo(position.X, position.Y, target.X, target.Y);

            // Use component pooling: reuse existing component or add new one
            if (Context.World.Has<MovementRequest>(Context.Entity.Value))
            {
                ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                request.Direction = direction;
                request.Active = true;
            }
            else
            {
                Context.World.Add(Context.Entity.Value, new MovementRequest(direction));
            }
        });
    }

    public override void OnUnload()
    {
        // Cleanup per-entity state
        if (Context.HasState<PatrolState>())
        {
            Context.RemoveState<PatrolState>();
        }

        Context.Logger.LogDebug("Patrol behavior deactivated");

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }
}

return new PatrolBehavior();
