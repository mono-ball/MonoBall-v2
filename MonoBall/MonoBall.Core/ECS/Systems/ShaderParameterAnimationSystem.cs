using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that animates shader parameters over time.
    /// Updates shader component parameters based on animation components.
    /// Fires ShaderAnimationCompletedEvent when non-looping animations complete.
    /// </summary>
    public class ShaderParameterAnimationSystem
        : BaseSystem<World, float>,
            IPrioritizedSystem,
            IDisposable
    {
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private readonly ILogger _logger;
        private readonly QueryDescription _entityShaderAnimationQuery;
        private readonly QueryDescription _layerShaderAnimationQuery;
        private readonly List<ShaderAnimationCompletedEvent> _completedEvents = new();
        private readonly List<Entity> _entitiesToRemoveAnimation = new();

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.ShaderParameterAnimation;

        /// <summary>
        /// Initializes a new instance of the ShaderParameterAnimationSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="shaderManagerSystem">The shader manager system for marking shaders dirty (optional).</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderParameterAnimationSystem(
            World world,
            ShaderManagerSystem? shaderManagerSystem,
            ILogger logger
        )
            : base(world)
        {
            _shaderManagerSystem = shaderManagerSystem;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _entityShaderAnimationQuery = new QueryDescription().WithAll<
                ShaderComponent,
                ShaderParameterAnimationComponent
            >();
            _layerShaderAnimationQuery = new QueryDescription().WithAll<
                RenderingShaderComponent,
                ShaderParameterAnimationComponent
            >();
        }

        /// <inheritdoc />
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime;
            _completedEvents.Clear();
            _entitiesToRemoveAnimation.Clear();

            // Animate per-entity shader parameters
            World.Query(
                in _entityShaderAnimationQuery,
                (
                    Entity entity,
                    ref ShaderComponent shader,
                    ref ShaderParameterAnimationComponent animation
                ) =>
                {
                    if (!shader.IsEnabled || !animation.IsEnabled)
                        return;

                    UpdateAnimation(
                        ref animation,
                        ref shader,
                        dt,
                        entity,
                        ShaderLayer.SpriteLayer,
                        shader.ShaderId
                    );
                }
            );

            // Animate layer shader parameters
            World.Query(
                in _layerShaderAnimationQuery,
                (
                    Entity entity,
                    ref RenderingShaderComponent shader,
                    ref ShaderParameterAnimationComponent animation
                ) =>
                {
                    if (!shader.IsEnabled || !animation.IsEnabled)
                        return;

                    UpdateAnimation(
                        ref animation,
                        ref shader,
                        dt,
                        entity,
                        shader.Layer,
                        shader.ShaderId
                    );
                }
            );

            // Remove animation components from completed entities (Arch ECS constraint: after query)
            foreach (var entity in _entitiesToRemoveAnimation)
            {
                if (World.IsAlive(entity) && World.Has<ShaderParameterAnimationComponent>(entity))
                {
                    World.Remove<ShaderParameterAnimationComponent>(entity);
                }
            }

            // Fire completion events AFTER query (Arch ECS constraint)
            foreach (var evt in _completedEvents)
            {
                var e = evt;
                EventBus.Send(ref e);
            }
        }

        private void UpdateAnimation(
            ref ShaderParameterAnimationComponent animation,
            ref ShaderComponent shader,
            float deltaTime,
            Entity entity,
            ShaderLayer layer,
            string shaderId
        )
        {
            // Get parameters dictionary (may be null)
            var parameters = shader.Parameters;

            UpdateAnimationCore(ref animation, ref parameters, deltaTime, entity, layer, shaderId);

            // Set parameters back (in case it was null and we created a new dictionary)
            shader.Parameters = parameters;
        }

        private void UpdateAnimation(
            ref ShaderParameterAnimationComponent animation,
            ref RenderingShaderComponent shader,
            float deltaTime,
            Entity entity,
            ShaderLayer layer,
            string shaderId
        )
        {
            // Get parameters dictionary (may be null)
            var parameters = shader.Parameters;

            UpdateAnimationCore(ref animation, ref parameters, deltaTime, entity, layer, shaderId);

            // Set parameters back (in case it was null and we created a new dictionary)
            shader.Parameters = parameters;
        }

        /// <summary>
        /// Core animation update logic shared between ShaderComponent and RenderingShaderComponent.
        /// </summary>
        private void UpdateAnimationCore(
            ref ShaderParameterAnimationComponent animation,
            ref System.Collections.Generic.Dictionary<string, object>? parameters,
            float deltaTime,
            Entity entity,
            ShaderLayer layer,
            string shaderId
        )
        {
            // Initialize parameters dictionary if needed
            if (parameters == null)
            {
                parameters = new System.Collections.Generic.Dictionary<string, object>();
            }

            if (!parameters.ContainsKey(animation.ParameterName))
            {
                // Initialize with start value if not present
                parameters[animation.ParameterName] = animation.StartValue;
            }

            // Update elapsed time
            animation.ElapsedTime += deltaTime;

            float progress;
            if (animation.Duration <= 0)
            {
                progress = 1.0f;
            }
            else if (animation.PingPong)
            {
                // Ping-pong: full cycle is Duration * 2 (forward then back)
                // Use modulo to wrap elapsed time and prevent overflow
                float cycleDuration = animation.Duration * 2.0f;
                animation.ElapsedTime = animation.ElapsedTime % cycleDuration;
                float cycleTime = animation.ElapsedTime;

                if (cycleTime > animation.Duration)
                {
                    // Second half: reverse direction (from 1.0 back to 0.0)
                    progress = 2.0f - (cycleTime / animation.Duration);
                }
                else
                {
                    // First half: forward direction (from 0.0 to 1.0)
                    progress = cycleTime / animation.Duration;
                }
            }
            else if (animation.IsLooping)
            {
                // Looping animation: wrap elapsed time using modulo to prevent overflow
                animation.ElapsedTime = animation.ElapsedTime % animation.Duration;
                progress = animation.ElapsedTime / animation.Duration;
            }
            else
            {
                // Non-looping animation: clamp elapsed time to duration to prevent overflow
                // This ensures the animation stays at end value after completion
                if (animation.ElapsedTime > animation.Duration)
                {
                    animation.ElapsedTime = animation.Duration;
                }
                progress = animation.ElapsedTime / animation.Duration;
            }

            // Apply easing
            float easedProgress = ApplyEasing(progress, animation.Easing);

            // Interpolate value
            object? interpolatedValue = Interpolate(
                animation.StartValue,
                animation.EndValue,
                easedProgress
            );

            if (interpolatedValue == null)
            {
                _logger.Warning(
                    "Failed to interpolate animation parameter {ParamName} for shader {ShaderId}",
                    animation.ParameterName,
                    shaderId
                );
                return;
            }

            // Get old value for event
            object? oldValue = parameters.TryGetValue(
                animation.ParameterName,
                out var existingValue
            )
                ? existingValue
                : null;

            // Update parameter
            parameters[animation.ParameterName] = interpolatedValue;

            // Fire parameter changed event
            var evt = new ShaderParameterChangedEvent
            {
                Layer = layer,
                ShaderId = shaderId,
                ParameterName = animation.ParameterName,
                OldValue = oldValue,
                NewValue = interpolatedValue,
                ShaderEntity = entity,
            };
            EventBus.Send(ref evt);

            // Check for completion (non-looping, non-pingpong animations only)
            if (
                !animation.IsLooping
                && !animation.PingPong
                && animation.ElapsedTime >= animation.Duration
            )
            {
                // Queue completion event (fired after query per Arch ECS constraint)
                _completedEvents.Add(
                    new ShaderAnimationCompletedEvent
                    {
                        Entity = entity,
                        ParameterName = animation.ParameterName,
                        ShaderId = shaderId,
                        Layer = layer,
                        FinalValue = interpolatedValue,
                    }
                );

                // Queue entity for animation component removal
                _entitiesToRemoveAnimation.Add(entity);
            }

            // Mark shaders dirty to ensure changes apply
            _shaderManagerSystem?.MarkShadersDirty();
        }

        private float ApplyEasing(float t, EasingFunction easing)
        {
            return easing switch
            {
                EasingFunction.Linear => t,
                EasingFunction.EaseIn => t * t,
                EasingFunction.EaseOut => 1.0f - (1.0f - t) * (1.0f - t),
                EasingFunction.EaseInOut => t < 0.5f
                    ? 2.0f * t * t
                    : 1.0f - MathF.Pow(-2.0f * t + 2.0f, 2.0f) / 2.0f,
                EasingFunction.SmoothStep => t * t * (3.0f - 2.0f * t),
                _ => t,
            };
        }

        private object? Interpolate(object startValue, object endValue, float t)
        {
            // Clamp t to [0, 1]
            t = Math.Clamp(t, 0.0f, 1.0f);

            return (startValue, endValue) switch
            {
                (float start, float end) => MathHelper.Lerp(start, end, t),
                (Vector2 start, Vector2 end) => Vector2.Lerp(start, end, t),
                (Vector3 start, Vector3 end) => Vector3.Lerp(start, end, t),
                (Vector4 start, Vector4 end) => Vector4.Lerp(start, end, t),
                (Color start, Color end) => Color.Lerp(start, end, t),
                _ => null,
            };
        }

        /// <summary>
        /// Disposes of system resources.
        /// </summary>
        public new void Dispose()
        {
            _completedEvents.Clear();
            _entitiesToRemoveAnimation.Clear();
        }
    }
}
