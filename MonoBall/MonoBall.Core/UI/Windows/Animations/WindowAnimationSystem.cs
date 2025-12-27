using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS;
using MonoBall.Core.Scenes.Systems;
using MonoBall.Core.UI.Windows.Animations.Events;
using Serilog;

namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// System responsible for updating window animation states.
    /// Updates WindowAnimationComponent values based on elapsed time and animation phases.
    /// </summary>
    public class WindowAnimationSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly ILogger _logger;
        private readonly QueryDescription _animationQuery;
        private readonly Func<SceneSystem?> _getSceneSystem;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.WindowAnimation;

        /// <summary>
        /// Initializes a new instance of the WindowAnimationSystem class.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="getSceneSystem">Function that returns the scene system (may be null if not yet initialized).</param>
        /// <exception cref="ArgumentNullException">Thrown when world or logger is null.</exception>
        public WindowAnimationSystem(World world, ILogger logger, Func<SceneSystem?> getSceneSystem)
            : base(world ?? throw new ArgumentNullException(nameof(world)))
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _getSceneSystem =
                getSceneSystem ?? throw new ArgumentNullException(nameof(getSceneSystem));
            _animationQuery = new QueryDescription().WithAll<WindowAnimationComponent>();
        }

        /// <summary>
        /// Updates window animation states.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            var sceneSystem = _getSceneSystem();
            bool isBlocked = false;

            // Check if updates are blocked by any scene
            if (sceneSystem != null)
            {
                isBlocked = sceneSystem.IsUpdateBlocked();
            }

            float dt = deltaTime;

            // Collect events to fire after query completes (Arch ECS doesn't allow structural changes during query)
            var eventsToFire = new List<AnimationEvent>();

            World.Query(
                in _animationQuery,
                (Entity entity, ref WindowAnimationComponent anim) =>
                {
                    // If updates are blocked, only update animations for windows belonging to blocking scenes
                    // Use centralized method from SceneSystem to avoid hardcoding component types
                    if (isBlocked && sceneSystem != null)
                    {
                        if (!sceneSystem.DoesEntityBelongToBlockingScene(anim.WindowEntity))
                        {
                            return; // Skip this animation - it doesn't belong to a blocking scene
                        }
                    }

                    UpdateAnimation(entity, ref anim, dt, eventsToFire);
                }
            );

            // Fire all collected events after query completes
            foreach (var evt in eventsToFire)
            {
                evt.Fire();
            }
        }

        /// <summary>
        /// Updates a single animation component.
        /// </summary>
        /// <param name="entity">The animation entity.</param>
        /// <param name="anim">The animation component (passed by reference).</param>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        /// <param name="eventsToFire">List to collect events that should be fired after query completes.</param>
        private void UpdateAnimation(
            Entity entity,
            ref WindowAnimationComponent anim,
            float deltaTime,
            List<AnimationEvent> eventsToFire
        )
        {
            // Validate Config.Phases is initialized
            if (anim.Config.Phases == null)
            {
                _logger.Warning(
                    "WindowAnimationComponent on entity {EntityId} has null Phases. Animation cannot run.",
                    entity.Id
                );
                return;
            }

            if (anim.Config.Phases.Count == 0)
            {
                _logger.Warning(
                    "WindowAnimationComponent on entity {EntityId} has empty Phases list. Animation cannot run.",
                    entity.Id
                );
                return;
            }

            if (anim.State == WindowAnimationState.NotStarted)
            {
                // Initialize animation
                anim.State = WindowAnimationState.Playing;
                anim.ElapsedTime = 0f;
                InitializeAnimation(ref anim);
                eventsToFire.Add(
                    new AnimationEvent
                    {
                        Type = AnimationEventType.Started,
                        Entity = entity,
                        WindowEntity = anim.WindowEntity,
                    }
                );
                return;
            }

            if (anim.State == WindowAnimationState.Paused)
            {
                return; // Don't update paused animations
            }

            if (anim.State != WindowAnimationState.Playing)
            {
                return; // Completed
            }

            anim.ElapsedTime += deltaTime;

            // Update current phase
            int currentPhaseIndex = GetCurrentPhaseIndex(ref anim);
            if (currentPhaseIndex < 0 || currentPhaseIndex >= anim.Config.Phases.Count)
            {
                // Animation complete - ensure final values are set exactly (prevents rounding issues)
                if (anim.Config.Phases != null && anim.Config.Phases.Count > 0)
                {
                    var lastPhase = anim.Config.Phases[anim.Config.Phases.Count - 1];
                    // Set final values explicitly to avoid any rounding issues
                    UpdateAnimationValues(ref anim, lastPhase, 1.0f);
                }

                anim.State = WindowAnimationState.Completed;
                eventsToFire.Add(
                    new AnimationEvent
                    {
                        Type = AnimationEventType.Completed,
                        Entity = entity,
                        WindowEntity = anim.WindowEntity,
                    }
                );

                if (anim.Config.DestroyOnComplete)
                {
                    eventsToFire.Add(
                        new AnimationEvent
                        {
                            Type = AnimationEventType.Destroy,
                            Entity = entity,
                            WindowEntity = anim.WindowEntity,
                        }
                    );
                }

                // Handle looping
                if (anim.Config.Loop)
                {
                    anim.ElapsedTime = 0f;
                    anim.State = WindowAnimationState.Playing;
                    InitializeAnimation(ref anim);
                    eventsToFire.Add(
                        new AnimationEvent
                        {
                            Type = AnimationEventType.Started,
                            Entity = entity,
                            WindowEntity = anim.WindowEntity,
                        }
                    );
                }
                return;
            }

            var phase = anim.Config.Phases[currentPhaseIndex];

            // Validate phase duration
            if (phase.Duration <= 0f || !float.IsFinite(phase.Duration))
            {
                _logger.Warning(
                    "Invalid phase duration {Duration} for animation entity {EntityId}, phase {PhaseIndex}. Skipping phase.",
                    phase.Duration,
                    entity.Id,
                    currentPhaseIndex
                );
                return;
            }

            float phaseStartTime = GetCumulativePhaseTime(ref anim, currentPhaseIndex);
            float phaseElapsedTime = anim.ElapsedTime - phaseStartTime;

            if (phaseElapsedTime < 0f)
            {
                return; // Haven't reached this phase yet
            }

            if (phase.Type == WindowAnimationType.Pause)
            {
                // Pause phase - just wait
                // No animation values to update
                return;
            }
            else if (phase.Type == WindowAnimationType.None)
            {
                // None type - instant transition to end values
                UpdateAnimationValues(ref anim, phase, 1.0f);
                return;
            }
            else
            {
                // Animate based on phase type
                float progress = Math.Min(phaseElapsedTime / phase.Duration, 1.0f);
                float easedProgress = ApplyEasing(progress, phase.Easing);
                UpdateAnimationValues(ref anim, phase, easedProgress);
            }
        }

        /// <summary>
        /// Initializes animation values from the first phase based on animation type.
        /// </summary>
        /// <param name="anim">The animation component to initialize.</param>
        private void InitializeAnimation(ref WindowAnimationComponent anim)
        {
            // Set initial values from first phase based on animation type
            if (anim.Config.Phases == null || anim.Config.Phases.Count == 0)
            {
                // Default values if no phases
                anim.PositionOffset = Vector2.Zero;
                anim.Scale = 1.0f;
                anim.Opacity = 1.0f;
                return;
            }

            var firstPhase = anim.Config.Phases[0];

            // Initialize based on animation type
            switch (firstPhase.Type)
            {
                case WindowAnimationType.Slide:
                    anim.PositionOffset = new Vector2(
                        firstPhase.StartValue.X,
                        firstPhase.StartValue.Y
                    );
                    anim.Scale = 1.0f;
                    anim.Opacity = 1.0f;
                    break;

                case WindowAnimationType.Fade:
                    anim.PositionOffset = Vector2.Zero;
                    anim.Scale = 1.0f;
                    anim.Opacity = firstPhase.StartValue.Z;
                    break;

                case WindowAnimationType.Scale:
                    anim.PositionOffset = Vector2.Zero;
                    anim.Scale = firstPhase.StartValue.Z;
                    anim.Opacity = 1.0f;
                    break;

                case WindowAnimationType.SlideFade:
                    anim.PositionOffset = new Vector2(
                        firstPhase.StartValue.X,
                        firstPhase.StartValue.Y
                    );
                    anim.Scale = 1.0f;
                    anim.Opacity = firstPhase.StartValue.Z;
                    break;

                case WindowAnimationType.SlideScale:
                    anim.PositionOffset = new Vector2(
                        firstPhase.StartValue.X,
                        firstPhase.StartValue.Y
                    );
                    anim.Scale = firstPhase.StartValue.Z;
                    anim.Opacity = 1.0f;
                    break;

                case WindowAnimationType.Pause:
                case WindowAnimationType.None:
                default:
                    // Use current values or defaults
                    anim.PositionOffset = Vector2.Zero;
                    anim.Scale = 1.0f;
                    anim.Opacity = 1.0f;
                    break;
            }
        }

        /// <summary>
        /// Updates animation values based on phase type and progress.
        /// </summary>
        /// <param name="anim">The animation component to update.</param>
        /// <param name="phase">The current animation phase.</param>
        /// <param name="progress">The animation progress (0.0 to 1.0, already eased).</param>
        private void UpdateAnimationValues(
            ref WindowAnimationComponent anim,
            WindowAnimationPhase phase,
            float progress
        )
        {
            switch (phase.Type)
            {
                case WindowAnimationType.Slide:
                    anim.PositionOffset = Vector2.Lerp(
                        new Vector2(phase.StartValue.X, phase.StartValue.Y),
                        new Vector2(phase.EndValue.X, phase.EndValue.Y),
                        progress
                    );
                    break;

                case WindowAnimationType.Fade:
                    anim.Opacity = MathHelper.Lerp(phase.StartValue.Z, phase.EndValue.Z, progress);
                    break;

                case WindowAnimationType.Scale:
                    anim.Scale = MathHelper.Lerp(phase.StartValue.Z, phase.EndValue.Z, progress);
                    break;

                case WindowAnimationType.SlideFade:
                    anim.PositionOffset = Vector2.Lerp(
                        new Vector2(phase.StartValue.X, phase.StartValue.Y),
                        new Vector2(phase.EndValue.X, phase.EndValue.Y),
                        progress
                    );
                    anim.Opacity = MathHelper.Lerp(phase.StartValue.Z, phase.EndValue.Z, progress);
                    break;

                case WindowAnimationType.SlideScale:
                    anim.PositionOffset = Vector2.Lerp(
                        new Vector2(phase.StartValue.X, phase.StartValue.Y),
                        new Vector2(phase.EndValue.X, phase.EndValue.Y),
                        progress
                    );
                    anim.Scale = MathHelper.Lerp(phase.StartValue.Z, phase.EndValue.Z, progress);
                    break;

                case WindowAnimationType.Pause:
                case WindowAnimationType.None:
                default:
                    // No animation values to update
                    break;
            }
        }

        /// <summary>
        /// Applies easing function to animation progress.
        /// </summary>
        /// <param name="t">The progress value (0.0 to 1.0).</param>
        /// <param name="easing">The easing type to apply.</param>
        /// <returns>The eased progress value.</returns>
        private float ApplyEasing(float t, WindowEasingType easing)
        {
            return easing switch
            {
                WindowEasingType.Linear => t,
                WindowEasingType.EaseIn => t * t,
                WindowEasingType.EaseOut => 1f - (1f - t) * (1f - t),
                WindowEasingType.EaseInOut => t < 0.5f
                    ? 2f * t * t
                    : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f,
                WindowEasingType.EaseInCubic => t * t * t,
                WindowEasingType.EaseOutCubic => 1f - MathF.Pow(1f - t, 3f),
                WindowEasingType.EaseInOutCubic => t < 0.5f
                    ? 4f * t * t * t
                    : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f,
                _ => t,
            };
        }

        /// <summary>
        /// Gets the index of the current animation phase based on elapsed time.
        /// </summary>
        /// <param name="anim">The animation component.</param>
        /// <returns>The current phase index, or Phases.Count if past all phases, or -1 if Phases is null or empty.</returns>
        private int GetCurrentPhaseIndex(ref WindowAnimationComponent anim)
        {
            if (anim.Config.Phases == null || anim.Config.Phases.Count == 0)
            {
                return -1;
            }

            // Calculate cumulative time as we iterate through phases
            float cumulativeTime = 0f;
            for (int i = 0; i < anim.Config.Phases.Count; i++)
            {
                cumulativeTime += anim.Config.Phases[i].Duration;
                if (anim.ElapsedTime < cumulativeTime)
                {
                    return i;
                }
            }
            return anim.Config.Phases.Count; // Past all phases
        }

        /// <summary>
        /// Gets the cumulative time up to (but not including) the specified phase index.
        /// </summary>
        /// <param name="anim">The animation component.</param>
        /// <param name="phaseIndex">The phase index (exclusive).</param>
        /// <returns>The cumulative time in seconds.</returns>
        private float GetCumulativePhaseTime(ref WindowAnimationComponent anim, int phaseIndex)
        {
            if (anim.Config.Phases == null)
            {
                return 0f;
            }

            float time = 0f;
            for (int i = 0; i < phaseIndex && i < anim.Config.Phases.Count; i++)
            {
                time += anim.Config.Phases[i].Duration;
            }
            return time;
        }

        /// <summary>
        /// Pauses the animation for the specified entity.
        /// </summary>
        /// <param name="entity">The animation entity.</param>
        /// <exception cref="InvalidOperationException">Thrown when entity doesn't have WindowAnimationComponent.</exception>
        public void PauseAnimation(Entity entity)
        {
            if (!World.Has<WindowAnimationComponent>(entity))
            {
                throw new InvalidOperationException(
                    $"Entity {entity.Id} does not have WindowAnimationComponent. Cannot pause animation."
                );
            }

            ref var anim = ref World.Get<WindowAnimationComponent>(entity);
            if (anim.State == WindowAnimationState.Playing)
            {
                anim.State = WindowAnimationState.Paused;
            }
        }

        /// <summary>
        /// Resumes a paused animation for the specified entity.
        /// </summary>
        /// <param name="entity">The animation entity.</param>
        /// <exception cref="InvalidOperationException">Thrown when entity doesn't have WindowAnimationComponent.</exception>
        public void ResumeAnimation(Entity entity)
        {
            if (!World.Has<WindowAnimationComponent>(entity))
            {
                throw new InvalidOperationException(
                    $"Entity {entity.Id} does not have WindowAnimationComponent. Cannot resume animation."
                );
            }

            ref var anim = ref World.Get<WindowAnimationComponent>(entity);
            if (anim.State == WindowAnimationState.Paused)
            {
                anim.State = WindowAnimationState.Playing;
            }
        }

        /// <summary>
        /// Internal helper struct for collecting events to fire after query completes.
        /// </summary>
        private struct AnimationEvent
        {
            public AnimationEventType Type;
            public Entity Entity;
            public Entity WindowEntity;

            public void Fire()
            {
                switch (Type)
                {
                    case AnimationEventType.Started:
                        var startedEvt = new WindowAnimationStartedEvent
                        {
                            Entity = Entity,
                            WindowEntity = WindowEntity,
                        };
                        EventBus.Send(ref startedEvt);
                        break;

                    case AnimationEventType.Completed:
                        var completedEvt = new WindowAnimationCompletedEvent
                        {
                            Entity = Entity,
                            WindowEntity = WindowEntity,
                        };
                        EventBus.Send(ref completedEvt);
                        break;

                    case AnimationEventType.Destroy:
                        var destroyEvt = new WindowAnimationDestroyEvent
                        {
                            Entity = Entity,
                            WindowEntity = WindowEntity,
                        };
                        EventBus.Send(ref destroyEvt);
                        break;
                }
            }
        }

        private enum AnimationEventType
        {
            Started,
            Completed,
            Destroy,
        }
    }
}
