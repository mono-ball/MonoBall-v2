using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Services;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that updates entity visibility based on flag values.
///     Reacts to flag changes and updates RenderableComponent.IsVisible accordingly.
/// </summary>
public class VisibilityFlagSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly IFlagVariableService _flagVariableService;
    private readonly ILogger _logger;
    private readonly QueryDescription _queryDescription;
    private bool _disposed;

    public VisibilityFlagSystem(
        World world,
        IFlagVariableService flagVariableService,
        ILogger logger
    )
        : base(world)
    {
        _flagVariableService =
            flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Query only NPCs in active maps for performance
        _queryDescription = new QueryDescription().WithAll<
            NpcComponent,
            RenderableComponent,
            ActiveMapEntity
        >();

        // Subscribe to flag changes using RefAction delegate
        EventBus.Subscribe<FlagChangedEvent>(OnFlagChanged);
    }

    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.VisibilityFlag;

    public override void Update(in float deltaTime)
    {
        // Check all NPCs with visibility flags on each update
        World.Query(
            in _queryDescription,
            (Entity entity, ref NpcComponent npc, ref RenderableComponent render) =>
            {
                // CRITICAL: Check if entity is still alive before accessing components
                // Entity might be destroyed or modified during query iteration (race condition)
                if (!World.IsAlive(entity))
                    return; // Entity was destroyed, skip

                if (string.IsNullOrWhiteSpace(npc.VisibilityFlag))
                    return;

                var flagValue = _flagVariableService.GetFlag(npc.VisibilityFlag);
                render.IsVisible = flagValue;
            }
        );
    }

    /// <summary>
    ///     Event handler for flag changes. Uses RefAction signature to match EventBus pattern.
    ///     Only reacts to global flags (Entity == null), not entity-specific flags.
    /// </summary>
    private void OnFlagChanged(ref FlagChangedEvent evt)
    {
        // Only handle global flags, not entity-specific flags
        if (evt.Entity.HasValue)
            return;

        // Copy event data to local variable (can't use ref parameter in lambda)
        var flagId = evt.FlagId;
        var newValue = evt.NewValue;

        // Reactively update entities when their visibility flag changes
        World.Query(
            in _queryDescription,
            (Entity entity, ref NpcComponent npc, ref RenderableComponent render) =>
            {
                // CRITICAL: Check if entity is still alive before accessing components
                // Entity might be destroyed or modified during query iteration (race condition)
                if (!World.IsAlive(entity))
                    return; // Entity was destroyed, skip

                if (npc.VisibilityFlag == flagId)
                    // Update visibility immediately
                    // Defensive check: Ensure entity still has RenderableComponent before modifying
                    if (World.Has<RenderableComponent>(entity))
                        render.IsVisible = newValue;
            }
        );
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
            EventBus.Unsubscribe<FlagChangedEvent>(OnFlagChanged);
        _disposed = true;
    }
}
