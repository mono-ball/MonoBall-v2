using Arch.Core;
using MonoBallFramework.Game.Ecs.Components.Movement;
using MonoBallFramework.Game.Ecs.Components.Rendering;
using MonoBallFramework.Game.Engine.Core.Events.System;
using MonoBallFramework.Game.Scripting.Runtime;

/// <summary>
/// Hidden/Invisible behavior - NPC is hidden and does not move.
/// State stored in per-entity HiddenState component (not instance fields).
/// Used for NPCs controlled by flags or events that start invisible.
/// Removes the Visible component to hide the entity from the render system.
/// </summary>
public class HiddenBehavior : ScriptBase
{
    // NO INSTANCE FIELDS! All state in components.

    public override void Initialize(ScriptContext ctx)
    {
        base.Initialize(ctx); // CRITICAL: Set Context property for event subscriptions
        ctx.Logger.LogDebug("Hidden behavior initialized (state will be created on first tick)");
    }

    public override void RegisterEventHandlers(ScriptContext ctx)
    {
        On<TickEvent>(evt =>
        {
            // Initialize state on first tick (when entity is available)
            if (!Context.HasState<HiddenState>())
            {
                // Remove Visible component to hide the entity from rendering
                bool hadVisibleComponent = Context.World.Has<Visible>(Context.Entity.Value);
                if (hadVisibleComponent)
                {
                    Context.World.Remove<Visible>(Context.Entity.Value);
                }

                Context.World.Add(
                    Context.Entity.Value,
                    new HiddenState
                    {
                        IsHidden = true,
                        Initialized = true,
                        HadVisibleComponent = hadVisibleComponent,
                    }
                );

                Context.Logger.LogInformation("Hidden behavior initialized | NPC hidden (Visible component removed)");
                return; // Skip first tick after initialization
            }

            // Get per-entity state (each NPC has its own)
            ref var state = ref Context.GetState<HiddenState>();

            // Ensure NPC doesn't move
            if (Context.World.Has<MovementRequest>(Context.Entity.Value))
            {
                ref var request = ref Context.World.Get<MovementRequest>(Context.Entity.Value);
                request.Active = false;
            }

            if (Context.World.Has<GridMovement>(Context.Entity.Value))
            {
                ref var movement = ref Context.World.Get<GridMovement>(Context.Entity.Value);
                movement.RunningState = RunningState.NotMoving;
            }
        });
    }

    public override void OnUnload()
    {
        // Restore Visible component if we removed it
        if (Context.HasState<HiddenState>())
        {
            ref var state = ref Context.GetState<HiddenState>();
            if (state.HadVisibleComponent && !Context.World.Has<Visible>(Context.Entity.Value))
            {
                Context.World.Add(Context.Entity.Value, new Visible());
                Context.Logger.LogDebug("Hidden behavior restored Visible component");
            }
            Context.RemoveState<HiddenState>();
        }

        Context.Logger.LogDebug("Hidden behavior deactivated");

        // CRITICAL: Dispose event subscriptions to prevent AccessViolationException on entity destruction
        base.OnUnload();
    }
}

// Component to store hidden-specific state
public struct HiddenState
{
    public bool IsHidden;
    public bool Initialized;
    public bool HadVisibleComponent; // Track if we removed the Visible component
}

return new HiddenBehavior();
