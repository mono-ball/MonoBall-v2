using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that animates shader parameters using keyframe-based timelines.
    /// Updates shader component parameters based on timeline components and keyframes.
    /// </summary>
    public class ShaderParameterTimelineSystem : BaseSystem<World, float>
    {
        private readonly ShaderManagerSystem? _shaderManagerSystem;
        private readonly ILogger _logger;
        private readonly QueryDescription _entityShaderTimelineQuery;
        private readonly QueryDescription _layerShaderTimelineQuery;

        // Keyframes storage: Entity -> List of keyframes
        private readonly Dictionary<Entity, List<ShaderParameterKeyframe>> _keyframes = new();

        /// <summary>
        /// Initializes a new instance of the ShaderParameterTimelineSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="shaderManagerSystem">The shader manager system for marking shaders dirty (optional).</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderParameterTimelineSystem(
            World world,
            ShaderManagerSystem? shaderManagerSystem,
            ILogger logger
        )
            : base(world)
        {
            _shaderManagerSystem = shaderManagerSystem;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _entityShaderTimelineQuery = new QueryDescription().WithAll<
                ShaderComponent,
                ShaderParameterTimelineComponent
            >();
            _layerShaderTimelineQuery = new QueryDescription().WithAll<
                LayerShaderComponent,
                ShaderParameterTimelineComponent
            >();
        }

        /// <inheritdoc />
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime;

            // Clean up keyframes for entities that no longer exist or no longer have timeline component
            CleanupDeadEntities();

            // Animate per-entity shader parameters
            World.Query(
                in _entityShaderTimelineQuery,
                (
                    Entity entity,
                    ref ShaderComponent shader,
                    ref ShaderParameterTimelineComponent timeline
                ) =>
                {
                    if (!shader.IsEnabled || !timeline.IsEnabled)
                        return;

                    // Ensure parameters dictionary exists
                    if (shader.Parameters == null)
                    {
                        shader.Parameters = new Dictionary<string, object>();
                    }
                    UpdateTimelineCommon(
                        ref timeline,
                        shader.Parameters,
                        dt,
                        entity,
                        ShaderLayer.SpriteLayer,
                        shader.ShaderId
                    );
                }
            );

            // Animate layer shader parameters
            World.Query(
                in _layerShaderTimelineQuery,
                (
                    Entity entity,
                    ref LayerShaderComponent shader,
                    ref ShaderParameterTimelineComponent timeline
                ) =>
                {
                    if (!shader.IsEnabled || !timeline.IsEnabled)
                        return;

                    // Ensure parameters dictionary exists
                    if (shader.Parameters == null)
                    {
                        shader.Parameters = new Dictionary<string, object>();
                    }
                    UpdateTimelineCommon(
                        ref timeline,
                        shader.Parameters,
                        dt,
                        entity,
                        shader.Layer,
                        shader.ShaderId
                    );
                }
            );
        }

        /// <summary>
        /// Cleans up keyframes for entities that no longer exist or no longer have timeline component.
        /// </summary>
        private void CleanupDeadEntities()
        {
            var entitiesToRemove = new List<Entity>();
            foreach (var entity in _keyframes.Keys)
            {
                if (!World.IsAlive(entity) || !World.Has<ShaderParameterTimelineComponent>(entity))
                {
                    entitiesToRemove.Add(entity);
                }
            }

            foreach (var entity in entitiesToRemove)
            {
                _keyframes.Remove(entity);
                _logger.Debug("Cleaned up keyframes for removed entity {EntityId}", entity.Id);
            }
        }

        /// <summary>
        /// Adds keyframes for an entity's timeline.
        /// </summary>
        /// <param name="entity">The entity with the timeline component.</param>
        /// <param name="keyframes">The list of keyframes to add.</param>
        public void AddKeyframes(Entity entity, List<ShaderParameterKeyframe> keyframes)
        {
            if (keyframes == null)
                throw new ArgumentNullException(nameof(keyframes));

            // Sort keyframes by time
            var sortedKeyframes = keyframes.OrderBy(k => k.Time).ToList();

            _keyframes[entity] = sortedKeyframes;

            // Update duration in component
            if (World.Has<ShaderParameterTimelineComponent>(entity))
            {
                ref var timeline = ref World.Get<ShaderParameterTimelineComponent>(entity);
                timeline.Duration = CalculateDuration(sortedKeyframes);
            }
        }

        /// <summary>
        /// Removes keyframes for an entity (called when component is removed).
        /// </summary>
        /// <param name="entity">The entity.</param>
        public void RemoveKeyframes(Entity entity)
        {
            _keyframes.Remove(entity);
        }

        /// <summary>
        /// Common timeline update logic shared between LayerShaderComponent and ShaderComponent.
        /// </summary>
        private void UpdateTimelineCommon(
            ref ShaderParameterTimelineComponent timeline,
            Dictionary<string, object> parameters,
            float deltaTime,
            Entity entity,
            ShaderLayer layer,
            string shaderId
        )
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            // Get keyframes for this entity
            if (!_keyframes.TryGetValue(entity, out var keyframes) || keyframes.Count == 0)
            {
                // No keyframes - nothing to animate
                return;
            }

            // Update elapsed time
            timeline.ElapsedTime += deltaTime;

            // Handle looping
            if (timeline.IsLooping && timeline.Duration > 0)
            {
                timeline.ElapsedTime %= timeline.Duration;
            }
            else if (timeline.Duration > 0)
            {
                // Clamp for non-looping
                timeline.ElapsedTime = Math.Min(timeline.ElapsedTime, timeline.Duration);
            }

            // Find current and next keyframes
            var (currentKeyframe, nextKeyframe) = FindKeyframes(keyframes, timeline.ElapsedTime);

            if (currentKeyframe == null)
            {
                // Before first keyframe - use first keyframe value
                if (keyframes.Count > 0)
                {
                    var firstKeyframe = keyframes[0];
                    parameters[timeline.ParameterName] = firstKeyframe.Value;
                }
                return;
            }

            if (nextKeyframe == null)
            {
                // After last keyframe - use last keyframe value
                parameters[timeline.ParameterName] = currentKeyframe.Value.Value;
                return;
            }

            // Interpolate between keyframes
            float t = CalculateInterpolationFactor(
                currentKeyframe.Value.Time,
                nextKeyframe.Value.Time,
                timeline.ElapsedTime,
                currentKeyframe.Value.Easing
            );

            var interpolatedValue = InterpolateKeyframes(
                currentKeyframe.Value,
                nextKeyframe.Value,
                t
            );

            if (interpolatedValue != null)
            {
                var oldValue = parameters.TryGetValue(timeline.ParameterName, out var old)
                    ? old
                    : null;
                parameters[timeline.ParameterName] = interpolatedValue;

                // Fire event if value changed
                if (!AreValuesEqual(oldValue, interpolatedValue))
                {
                    var evt = new ShaderParameterChangedEvent
                    {
                        Layer = layer,
                        ShaderId = shaderId,
                        ParameterName = timeline.ParameterName,
                        OldValue = oldValue,
                        NewValue = interpolatedValue,
                        ShaderEntity = entity,
                    };
                    EventBus.Send(ref evt);
                }

                // Mark shaders dirty to ensure changes apply
                _shaderManagerSystem?.MarkShadersDirty();
            }
        }

        private (ShaderParameterKeyframe?, ShaderParameterKeyframe?) FindKeyframes(
            List<ShaderParameterKeyframe> keyframes,
            float time
        )
        {
            if (keyframes.Count == 0)
                return (null, null);

            if (keyframes.Count == 1)
                return (keyframes[0], null);

            // Find keyframes that bracket the current time
            for (int i = 0; i < keyframes.Count - 1; i++)
            {
                if (time >= keyframes[i].Time && time <= keyframes[i + 1].Time)
                {
                    return (keyframes[i], keyframes[i + 1]);
                }
            }

            // Time is before first keyframe
            if (time < keyframes[0].Time)
                return (null, keyframes[0]);

            // Time is after last keyframe
            return (keyframes[keyframes.Count - 1], null);
        }

        private float CalculateInterpolationFactor(
            float startTime,
            float endTime,
            float currentTime,
            EasingFunction easing
        )
        {
            if (endTime <= startTime)
                return 1.0f;

            float t = (currentTime - startTime) / (endTime - startTime);
            t = Math.Clamp(t, 0.0f, 1.0f);

            // Apply easing
            return ApplyEasing(t, easing);
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

        private object? InterpolateKeyframes(
            ShaderParameterKeyframe current,
            ShaderParameterKeyframe next,
            float t
        )
        {
            return (current.Value, next.Value) switch
            {
                (float start, float end) => MathHelper.Lerp(start, end, t),
                (Vector2 start, Vector2 end) => Vector2.Lerp(start, end, t),
                (Vector3 start, Vector3 end) => Vector3.Lerp(start, end, t),
                (Vector4 start, Vector4 end) => Vector4.Lerp(start, end, t),
                (Color start, Color end) => Color.Lerp(start, end, t),
                _ => null,
            };
        }

        private float CalculateDuration(List<ShaderParameterKeyframe> keyframes)
        {
            if (keyframes.Count == 0)
                return 0.0f;

            return keyframes.Max(k => k.Time);
        }

        private static bool AreValuesEqual(object? value1, object? value2)
        {
            if (value1 == null && value2 == null)
                return true;
            if (value1 == null || value2 == null)
                return false;

            // For value types, use Equals()
            if (value1.GetType().IsValueType)
            {
                return value1.Equals(value2);
            }

            // For reference types, use reference equality
            return ReferenceEquals(value1, value2);
        }
    }
}
