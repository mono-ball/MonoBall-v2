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
    /// System that executes sequenced shader animation phases.
    /// Phase and animation data are stored externally to avoid List&lt;T&gt; allocations in ECS components.
    /// Follows the TimelineSystem pattern for external storage.
    /// </summary>
    public class ShaderAnimationChainSystem
        : BaseSystem<World, float>,
            IPrioritizedSystem,
            IDisposable
    {
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private readonly ILogger _logger;
        private readonly QueryDescription _entityShaderQuery;
        private readonly QueryDescription _layerShaderQuery;

        // External storage for phases (avoids List<T> in component struct)
        private readonly Dictionary<Entity, List<ShaderAnimationPhaseData>> _phases = new();
        private readonly Dictionary<
            Entity,
            Dictionary<int, List<ShaderAnimationData>>
        > _phaseAnimations = new();

        private readonly List<Entity> _deadEntities = new();
        private readonly List<ShaderAnimationPhaseCompletedEvent> _phaseCompletedEvents = new();
        private readonly List<ShaderAnimationChainCompletedEvent> _chainCompletedEvents = new();

        /// <summary>
        /// Data for a single phase in the animation chain.
        /// </summary>
        public struct ShaderAnimationPhaseData
        {
            public float Delay { get; set; }
            public float Duration { get; set; }
        }

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.ShaderAnimationChain;

        /// <summary>
        /// Initializes a new instance of the ShaderAnimationChainSystem.
        /// </summary>
        public ShaderAnimationChainSystem(
            World world,
            ShaderManagerSystem? shaderManagerSystem,
            ILogger logger
        )
            : base(world)
        {
            _shaderManagerSystem = shaderManagerSystem;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _entityShaderQuery = new QueryDescription().WithAll<
                ShaderComponent,
                ShaderAnimationChainComponent
            >();
            _layerShaderQuery = new QueryDescription().WithAll<
                RenderingShaderComponent,
                ShaderAnimationChainComponent
            >();
        }

        /// <summary>
        /// Sets the animation chain for an entity.
        /// </summary>
        /// <param name="entity">The entity to set chain for.</param>
        /// <param name="phases">The phase data (delays and durations).</param>
        /// <param name="animations">The animations for each phase (keyed by phase index).</param>
        public void SetChain(
            Entity entity,
            List<ShaderAnimationPhaseData> phases,
            Dictionary<int, List<ShaderAnimationData>> animations
        )
        {
            _phases[entity] = phases;
            _phaseAnimations[entity] = animations;
        }

        /// <summary>
        /// Clears the animation chain for an entity.
        /// </summary>
        /// <param name="entity">The entity to clear chain for.</param>
        public void ClearChain(Entity entity)
        {
            _phases.Remove(entity);
            _phaseAnimations.Remove(entity);
        }

        /// <inheritdoc />
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime;
            _phaseCompletedEvents.Clear();
            _chainCompletedEvents.Clear();
            _deadEntities.Clear();

            // Clean up dead entities
            foreach (var kvp in _phases)
            {
                if (!World.IsAlive(kvp.Key))
                {
                    _deadEntities.Add(kvp.Key);
                }
            }
            foreach (var entity in _deadEntities)
            {
                _phases.Remove(entity);
                _phaseAnimations.Remove(entity);
            }

            // Process entity shaders
            World.Query(
                in _entityShaderQuery,
                (
                    Entity entity,
                    ref ShaderComponent shader,
                    ref ShaderAnimationChainComponent chain
                ) =>
                {
                    if (
                        !shader.IsEnabled
                        || !chain.IsEnabled
                        || chain.State != ShaderAnimationChainState.Playing
                    )
                        return;

                    // Ensure dictionary exists (can't pass property by ref)
                    shader.Parameters ??= new Dictionary<string, object>();

                    UpdateChain(
                        entity,
                        ref chain,
                        shader.Parameters,
                        dt,
                        ShaderLayer.SpriteLayer,
                        shader.ShaderId
                    );
                }
            );

            // Process layer shaders
            World.Query(
                in _layerShaderQuery,
                (
                    Entity entity,
                    ref RenderingShaderComponent shader,
                    ref ShaderAnimationChainComponent chain
                ) =>
                {
                    if (
                        !shader.IsEnabled
                        || !chain.IsEnabled
                        || chain.State != ShaderAnimationChainState.Playing
                    )
                        return;

                    // Ensure dictionary exists (can't pass property by ref)
                    shader.Parameters ??= new Dictionary<string, object>();

                    UpdateChain(
                        entity,
                        ref chain,
                        shader.Parameters,
                        dt,
                        shader.Layer,
                        shader.ShaderId
                    );
                }
            );

            // Fire events AFTER query (Arch ECS constraint)
            foreach (var evt in _phaseCompletedEvents)
            {
                var e = evt;
                EventBus.Send(ref e);
            }
            foreach (var evt in _chainCompletedEvents)
            {
                var e = evt;
                EventBus.Send(ref e);
            }
        }

        private void UpdateChain(
            Entity entity,
            ref ShaderAnimationChainComponent chain,
            Dictionary<string, object> parameters,
            float deltaTime,
            ShaderLayer layer,
            string shaderId
        )
        {
            if (!_phases.TryGetValue(entity, out var phases) || phases.Count == 0)
                return;

            if (!_phaseAnimations.TryGetValue(entity, out var phaseAnimations))
                return;

            // Update elapsed time
            chain.PhaseElapsedTime += deltaTime;

            var currentPhase = phases[chain.CurrentPhaseIndex];
            float phaseStartTime = currentPhase.Delay;
            float phaseEndTime = phaseStartTime + currentPhase.Duration;

            // Check if still in delay
            if (chain.PhaseElapsedTime < phaseStartTime)
            {
                return;
            }

            // Get animations for current phase
            if (!phaseAnimations.TryGetValue(chain.CurrentPhaseIndex, out var animations))
            {
                animations = new List<ShaderAnimationData>();
            }

            // Update animations
            float phaseProgress = chain.PhaseElapsedTime - phaseStartTime;
            bool anyChanged = false;

            for (int i = 0; i < animations.Count; i++)
            {
                var animation = animations[i];
                animation.ElapsedTime = phaseProgress;

                if (
                    !ShaderAnimationUtilities.UpdateAnimation(
                        ref animation,
                        0, // Don't add deltaTime again, we set ElapsedTime directly
                        out var interpolatedValue,
                        out _
                    )
                )
                {
                    continue;
                }

                if (interpolatedValue != null)
                {
                    parameters[animation.ParameterName] = interpolatedValue;
                    anyChanged = true;
                }
            }

            // Check for phase completion
            if (chain.PhaseElapsedTime >= phaseEndTime)
            {
                int totalPhases = phases.Count;
                bool hasMorePhases = chain.CurrentPhaseIndex < totalPhases - 1;

                _phaseCompletedEvents.Add(
                    new ShaderAnimationPhaseCompletedEvent
                    {
                        Entity = entity,
                        ShaderId = shaderId,
                        PhaseIndex = chain.CurrentPhaseIndex,
                        TotalPhases = totalPhases,
                        HasMorePhases = hasMorePhases,
                    }
                );

                if (hasMorePhases)
                {
                    // Move to next phase
                    chain.CurrentPhaseIndex++;
                    chain.PhaseElapsedTime = 0f;
                }
                else if (chain.IsLooping)
                {
                    // Loop back to start
                    chain.CurrentPhaseIndex = 0;
                    chain.PhaseElapsedTime = 0f;
                }
                else
                {
                    // Chain completed
                    chain.State = ShaderAnimationChainState.Completed;

                    _chainCompletedEvents.Add(
                        new ShaderAnimationChainCompletedEvent
                        {
                            Entity = entity,
                            ShaderId = shaderId,
                            TotalPhasesExecuted = totalPhases,
                            WasLooping = false,
                        }
                    );
                }
            }

            if (anyChanged)
            {
                _shaderManagerSystem?.MarkShadersDirty();
            }
        }

        /// <summary>
        /// Stops an animation chain on an entity.
        /// </summary>
        /// <param name="entity">The entity to stop.</param>
        public void StopChain(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<ShaderAnimationChainComponent>(entity))
                return;

            ref var chain = ref World.Get<ShaderAnimationChainComponent>(entity);
            chain.State = ShaderAnimationChainState.Stopped;
        }

        /// <summary>
        /// Pauses an animation chain on an entity.
        /// </summary>
        /// <param name="entity">The entity to pause.</param>
        public void PauseChain(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<ShaderAnimationChainComponent>(entity))
                return;

            ref var chain = ref World.Get<ShaderAnimationChainComponent>(entity);
            if (chain.State == ShaderAnimationChainState.Playing)
            {
                chain.State = ShaderAnimationChainState.Paused;
            }
        }

        /// <summary>
        /// Resumes a paused animation chain on an entity.
        /// </summary>
        /// <param name="entity">The entity to resume.</param>
        public void ResumeChain(Entity entity)
        {
            if (!World.IsAlive(entity) || !World.Has<ShaderAnimationChainComponent>(entity))
                return;

            ref var chain = ref World.Get<ShaderAnimationChainComponent>(entity);
            if (chain.State == ShaderAnimationChainState.Paused)
            {
                chain.State = ShaderAnimationChainState.Playing;
            }
        }

        /// <summary>
        /// Disposes of system resources.
        /// </summary>
        public new void Dispose()
        {
            _phases.Clear();
            _phaseAnimations.Clear();
            _deadEntities.Clear();
            _phaseCompletedEvents.Clear();
            _chainCompletedEvents.Clear();
        }
    }
}
