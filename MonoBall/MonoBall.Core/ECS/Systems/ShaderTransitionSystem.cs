using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Utilities;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that handles shader transitions with crossfade blending.
    /// Updates transition progress and blend weights for dual-render blending.
    /// </summary>
    public class ShaderTransitionSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
    {
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private readonly ILogger _logger;
        private readonly QueryDescription _transitionQuery;
        private readonly List<ShaderTransitionCompletedEvent> _completedEvents = new();
        private readonly List<Entity> _entitiesToCleanup = new();

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.ShaderTransition;

        /// <summary>
        /// Initializes a new instance of the ShaderTransitionSystem.
        /// </summary>
        public ShaderTransitionSystem(
            World world,
            ShaderManagerSystem? shaderManagerSystem,
            ILogger logger
        )
            : base(world)
        {
            _shaderManagerSystem = shaderManagerSystem;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _transitionQuery = new QueryDescription().WithAll<
                RenderingShaderComponent,
                ShaderTransitionComponent
            >();
        }

        /// <inheritdoc />
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime;
            _completedEvents.Clear();
            _entitiesToCleanup.Clear();

            World.Query(
                in _transitionQuery,
                (
                    Entity entity,
                    ref RenderingShaderComponent shader,
                    ref ShaderTransitionComponent transition
                ) =>
                {
                    if (
                        transition.State == ShaderTransitionState.Completed
                        || transition.State == ShaderTransitionState.Cancelled
                    )
                    {
                        _entitiesToCleanup.Add(entity);
                        return;
                    }

                    // Start transition if not started
                    if (transition.State == ShaderTransitionState.NotStarted)
                    {
                        transition.State = ShaderTransitionState.InProgress;
                    }

                    // Update elapsed time
                    transition.ElapsedTime += dt;

                    // Calculate blend weight
                    float progress;
                    if (transition.Duration <= 0)
                    {
                        progress = 1.0f;
                    }
                    else
                    {
                        progress = Math.Clamp(transition.ElapsedTime / transition.Duration, 0f, 1f);
                    }

                    // Apply easing
                    transition.BlendWeight = ShaderAnimationUtilities.ApplyEasing(
                        progress,
                        transition.Easing
                    );

                    // Check for completion
                    if (transition.ElapsedTime >= transition.Duration)
                    {
                        transition.State = ShaderTransitionState.Completed;
                        transition.BlendWeight = 1.0f;

                        // Swap to target shader
                        if (!string.IsNullOrEmpty(transition.ToShaderId))
                        {
                            shader.ShaderId = transition.ToShaderId;
                        }

                        _completedEvents.Add(
                            new ShaderTransitionCompletedEvent
                            {
                                Entity = entity,
                                FromShaderId = transition.FromShaderId,
                                ToShaderId = transition.ToShaderId ?? string.Empty,
                                Layer = shader.Layer,
                            }
                        );

                        _entitiesToCleanup.Add(entity);
                    }

                    _shaderManagerSystem?.MarkShadersDirty();
                }
            );

            // Remove transition components from completed entities
            foreach (var entity in _entitiesToCleanup)
            {
                if (World.IsAlive(entity) && World.Has<ShaderTransitionComponent>(entity))
                {
                    World.Remove<ShaderTransitionComponent>(entity);
                }
            }

            // Fire completion events AFTER query (Arch ECS constraint)
            foreach (var evt in _completedEvents)
            {
                var e = evt;
                EventBus.Send(ref e);
            }
        }

        /// <summary>
        /// Starts a transition on an entity.
        /// </summary>
        /// <param name="entity">The entity with RenderingShaderComponent.</param>
        /// <param name="fromShaderId">The source shader ID (can be null).</param>
        /// <param name="toShaderId">The target shader ID.</param>
        /// <param name="duration">Transition duration in seconds.</param>
        /// <param name="easing">The easing function.</param>
        public void StartTransition(
            Entity entity,
            string? fromShaderId,
            string toShaderId,
            float duration,
            EasingFunction easing = EasingFunction.Linear
        )
        {
            if (!World.IsAlive(entity))
            {
                _logger.Warning("Cannot start transition on dead entity {EntityId}", entity.Id);
                return;
            }

            var transition = ShaderTransitionComponent.Create(
                fromShaderId,
                toShaderId,
                duration,
                easing
            );

            if (World.Has<ShaderTransitionComponent>(entity))
            {
                World.Set(entity, transition);
            }
            else
            {
                World.Add(entity, transition);
            }

            _logger.Debug(
                "Started shader transition from {From} to {To} over {Duration}s",
                fromShaderId ?? "none",
                toShaderId,
                duration
            );
        }

        /// <summary>
        /// Cancels any active transition on an entity.
        /// </summary>
        /// <param name="entity">The entity to cancel transition on.</param>
        public void CancelTransition(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<ShaderTransitionComponent>(entity))
                return;

            ref var transition = ref World.Get<ShaderTransitionComponent>(entity);
            transition.State = ShaderTransitionState.Cancelled;
        }

        /// <summary>
        /// Gets the current blend weight for an entity's transition.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>Blend weight (0.0-1.0), or 0 if no transition active.</returns>
        public float GetBlendWeight(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<ShaderTransitionComponent>(entity))
                return 0f;

            return World.Get<ShaderTransitionComponent>(entity).BlendWeight;
        }

        /// <summary>
        /// Checks if an entity has an active transition.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if transition is in progress.</returns>
        public bool IsTransitioning(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<ShaderTransitionComponent>(entity))
                return false;

            var state = World.Get<ShaderTransitionComponent>(entity).State;
            return state == ShaderTransitionState.NotStarted
                || state == ShaderTransitionState.InProgress;
        }

        /// <summary>
        /// Disposes of system resources.
        /// </summary>
        public new void Dispose()
        {
            _completedEvents.Clear();
            _entitiesToCleanup.Clear();
        }
    }
}
