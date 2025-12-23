using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Logging;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that updates entity visibility based on flag values.
    /// Reacts to flag changes and updates RenderableComponent.IsVisible accordingly.
    /// </summary>
    public class VisibilityFlagSystem : BaseSystem<World, float>, IDisposable
    {
        private readonly IFlagVariableService _flagVariableService;
        private readonly QueryDescription _queryDescription;
        private readonly ILogger _logger;
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
            _queryDescription = new QueryDescription().WithAll<NpcComponent, RenderableComponent>();

            // Subscribe to flag changes using RefAction delegate
            EventBus.Subscribe<FlagChangedEvent>(OnFlagChanged);
        }

        public override void Update(in float deltaTime)
        {
            // Check all NPCs with visibility flags on each update
            World.Query(
                in _queryDescription,
                (Entity entity, ref NpcComponent npc, ref RenderableComponent render) =>
                {
                    if (string.IsNullOrWhiteSpace(npc.VisibilityFlag))
                        return;

                    bool flagValue = _flagVariableService.GetFlag(npc.VisibilityFlag);
                    render.IsVisible = flagValue;
                }
            );
        }

        /// <summary>
        /// Event handler for flag changes. Uses RefAction signature to match EventBus pattern.
        /// Only reacts to global flags (Entity == null), not entity-specific flags.
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
                    if (npc.VisibilityFlag == flagId)
                    {
                        // Update visibility immediately
                        render.IsVisible = newValue;
                    }
                }
            );
        }

        public new void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                EventBus.Unsubscribe<FlagChangedEvent>(OnFlagChanged);
            }
            _disposed = true;
        }
    }
}
