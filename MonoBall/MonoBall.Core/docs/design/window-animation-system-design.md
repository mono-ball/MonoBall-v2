# Window Animation System Design

## Overview

Design a general window animation system that supports various animation types (slide, fade, scale, etc.) and integrates with the UI Window System. The system must be compatible with Arch ECS architecture and event-driven patterns, and support migration from the existing `PopupAnimationComponent`.

## Goals

1. **Generalize Animation**: Support multiple animation types beyond slide down/up
2. **ECS Integration**: Use Arch ECS components and systems
3. **Event-Driven**: Fire events for animation lifecycle (start, complete, state changes)
4. **Migration Path**: Migrate `PopupAnimationComponent` to use the new system
5. **Window Integration**: Work seamlessly with `WindowRenderer` and `WindowBounds`
6. **Extensibility**: Easy to add new animation types

## Architecture

### Core Components

#### WindowAnimationComponent

```csharp
namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Component that tracks window animation state and timing.
    /// Attached to entities that have animated windows.
    /// </summary>
    public struct WindowAnimationComponent
    {
        /// <summary>
        /// Gets or sets the current animation state.
        /// </summary>
        public WindowAnimationState State { get; set; }

        /// <summary>
        /// Gets or sets the time elapsed in the current animation state.
        /// </summary>
        public float ElapsedTime { get; set; }

        /// <summary>
        /// Gets or sets the animation configuration.
        /// Defines animation type, durations, easing, and parameters.
        /// </summary>
        public WindowAnimationConfig Config { get; set; }

        /// <summary>
        /// Gets or sets the current animated position offset (X, Y).
        /// Applied to window position during rendering.
        /// </summary>
        public Vector2 PositionOffset { get; set; }

        /// <summary>
        /// Gets or sets the current animated scale factor.
        /// Applied to window size during rendering (1.0 = normal size).
        /// </summary>
        public float Scale { get; set; }

        /// <summary>
        /// Gets or sets the current animated opacity (0.0 = transparent, 1.0 = opaque).
        /// Applied to window rendering.
        /// </summary>
        public float Opacity { get; set; }

        /// <summary>
        /// Gets or sets the window entity this animation applies to.
        /// Used to reference the window being animated.
        /// Must be validated (World.IsAlive) before use if window entity may be destroyed.
        /// </summary>
        public Entity WindowEntity { get; set; }
    }
}
```

#### WindowAnimationConfig

```csharp
namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Configuration for window animations.
    /// Defines animation sequence, timing, and parameters.
    /// </summary>
    public struct WindowAnimationConfig
    {
        /// <summary>
        /// Gets or sets the animation sequence (list of animation phases).
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public List<WindowAnimationPhase> Phases { get; set; }

        /// <summary>
        /// Gets or sets the window dimensions (used for position calculations).
        /// </summary>
        public Vector2 WindowSize { get; set; }

        /// <summary>
        /// Gets or sets the initial window position (before animation).
        /// </summary>
        public Vector2 InitialPosition { get; set; }

        /// <summary>
        /// Gets or sets whether the animation loops.
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// Gets or sets whether to destroy the window entity when animation completes.
        /// </summary>
        public bool DestroyOnComplete { get; set; }
    }
}
```

#### WindowAnimationPhase

```csharp
namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Represents a single phase in a window animation sequence.
    /// </summary>
    public struct WindowAnimationPhase
    {
        /// <summary>
        /// Gets or sets the animation type for this phase.
        /// </summary>
        public WindowAnimationType Type { get; set; }

        /// <summary>
        /// Gets or sets the duration of this phase in seconds.
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        /// Gets or sets the easing function for this phase.
        /// </summary>
        public WindowEasingType Easing { get; set; }

        /// <summary>
        /// Gets or sets the start value for this phase.
        /// Field usage depends on animation type:
        /// - Slide: X, Y = position offset, Z = unused
        /// - Fade: Z = opacity (0.0-1.0), X, Y = unused
        /// - Scale: Z = scale factor (1.0 = normal), X, Y = unused
        /// - SlideFade: X, Y = position offset, Z = opacity (0.0-1.0)
        /// - SlideScale: X, Y = position offset, Z = scale factor
        /// - Pause: X, Y, Z = unused
        /// </summary>
        public Vector3 StartValue { get; set; }

        /// <summary>
        /// Gets or sets the end value for this phase.
        /// Field usage matches StartValue based on animation type.
        /// </summary>
        public Vector3 EndValue { get; set; }

        /// <summary>
        /// Gets or sets optional parameters specific to animation type.
        /// </summary>
        public Dictionary<string, object>? Parameters { get; set; }
    }
}
```

#### WindowAnimationType (Enum)

```csharp
namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Types of window animations supported.
    /// </summary>
    public enum WindowAnimationType
    {
        /// <summary>
        /// No animation (instant transition).
        /// </summary>
        None,

        /// <summary>
        /// Slide animation (position change).
        /// </summary>
        Slide,

        /// <summary>
        /// Fade animation (opacity change).
        /// </summary>
        Fade,

        /// <summary>
        /// Scale animation (size change).
        /// </summary>
        Scale,

        /// <summary>
        /// Combined slide and fade animation.
        /// </summary>
        SlideFade,

        /// <summary>
        /// Combined slide and scale animation.
        /// </summary>
        SlideScale,

        /// <summary>
        /// Pause/hold at current state (no animation, just wait).
        /// </summary>
        Pause,
    }
}
```

#### WindowAnimationState (Enum)

```csharp
namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Current state of a window animation.
    /// </summary>
    public enum WindowAnimationState
    {
        /// <summary>
        /// Animation is not started yet.
        /// </summary>
        NotStarted,

        /// <summary>
        /// Animation is currently playing.
        /// </summary>
        Playing,

        /// <summary>
        /// Animation is paused (can be resumed).
        /// </summary>
        Paused,

        /// <summary>
        /// Animation has completed.
        /// </summary>
        Completed,
    }
}
```

#### WindowEasingType (Enum)

```csharp
namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Easing functions for window animations.
    /// </summary>
    public enum WindowEasingType
    {
        /// <summary>
        /// Linear interpolation (no easing).
        /// </summary>
        Linear,

        /// <summary>
        /// Ease in (slow start, fast end).
        /// </summary>
        EaseIn,

        /// <summary>
        /// Ease out (fast start, slow end).
        /// </summary>
        EaseOut,

        /// <summary>
        /// Ease in-out (slow start and end, fast middle).
        /// </summary>
        EaseInOut,

        /// <summary>
        /// Cubic ease in.
        /// </summary>
        EaseInCubic,

        /// <summary>
        /// Cubic ease out.
        /// </summary>
        EaseOutCubic,

        /// <summary>
        /// Cubic ease in-out.
        /// </summary>
        EaseInOutCubic,
    }
}
```

### Core System

#### WindowAnimationSystem

```csharp
using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS;
using MonoBall.Core.UI.Windows.Animations.Events;
using Serilog;

namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// System responsible for updating window animation states.
    /// Updates WindowAnimationComponent values based on elapsed time and animation phases.
    /// </summary>
    public class WindowAnimationSystem : BaseSystem<World, float>
    {
        private readonly ILogger _logger;
        private readonly QueryDescription _animationQuery;

        /// <summary>
        /// Initializes a new instance of the WindowAnimationSystem class.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="logger">The logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when world or logger is null.</exception>
        public WindowAnimationSystem(World world, ILogger logger) : base(world)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _animationQuery = new QueryDescription().WithAll<WindowAnimationComponent>();
        }

        /// <summary>
        /// Updates window animation states.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime;
            
            // Collect events to fire after query completes (Arch ECS doesn't allow structural changes during query)
            var eventsToFire = new List<AnimationEvent>();

            World.Query(
                in _animationQuery,
                (Entity entity, ref WindowAnimationComponent anim) =>
                {
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
                eventsToFire.Add(new AnimationEvent
                {
                    Type = AnimationEventType.Started,
                    Entity = entity,
                    WindowEntity = anim.WindowEntity,
                });
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
                // Animation complete
                anim.State = WindowAnimationState.Completed;
                eventsToFire.Add(new AnimationEvent
                {
                    Type = AnimationEventType.Completed,
                    Entity = entity,
                    WindowEntity = anim.WindowEntity,
                });

                if (anim.Config.DestroyOnComplete)
                {
                    eventsToFire.Add(new AnimationEvent
                    {
                        Type = AnimationEventType.Destroy,
                        Entity = entity,
                        WindowEntity = anim.WindowEntity,
                    });
                }

                // Handle looping
                if (anim.Config.Loop)
                {
                    anim.ElapsedTime = 0f;
                    anim.State = WindowAnimationState.Playing;
                    InitializeAnimation(ref anim);
                    eventsToFire.Add(new AnimationEvent
                    {
                        Type = AnimationEventType.Started,
                        Entity = entity,
                        WindowEntity = anim.WindowEntity,
                    });
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
                    anim.PositionOffset = new Vector2(firstPhase.StartValue.X, firstPhase.StartValue.Y);
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
                    anim.PositionOffset = new Vector2(firstPhase.StartValue.X, firstPhase.StartValue.Y);
                    anim.Scale = 1.0f;
                    anim.Opacity = firstPhase.StartValue.Z;
                    break;

                case WindowAnimationType.SlideScale:
                    anim.PositionOffset = new Vector2(firstPhase.StartValue.X, firstPhase.StartValue.Y);
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
                WindowEasingType.EaseInOut => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f,
                WindowEasingType.EaseInCubic => t * t * t,
                WindowEasingType.EaseOutCubic => 1f - MathF.Pow(1f - t, 3f),
                WindowEasingType.EaseInOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f,
                _ => t,
            };
        }

        /// <summary>
        /// Gets the index of the current animation phase based on elapsed time.
        /// </summary>
        /// <param name="anim">The animation component.</param>
        /// <returns>The current phase index, or Phases.Count if past all phases.</returns>
        private int GetCurrentPhaseIndex(ref WindowAnimationComponent anim)
        {
            if (anim.Config.Phases == null)
            {
                return -1;
            }

            float cumulativeTime = GetCumulativePhaseTime(ref anim, anim.Config.Phases.Count);
            if (anim.ElapsedTime >= cumulativeTime)
            {
                return anim.Config.Phases.Count; // Past all phases
            }

            for (int i = 0; i < anim.Config.Phases.Count; i++)
            {
                cumulativeTime = GetCumulativePhaseTime(ref anim, i + 1);
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
```

### Events

#### WindowAnimationStartedEvent

```csharp
namespace MonoBall.Core.UI.Windows.Animations.Events
{
    /// <summary>
    /// Event fired when a window animation starts.
    /// </summary>
    public struct WindowAnimationStartedEvent
    {
        /// <summary>
        /// Gets or sets the animation entity.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// Gets or sets the window entity being animated.
        /// </summary>
        public Entity WindowEntity { get; set; }
    }
}
```

#### WindowAnimationCompletedEvent

```csharp
namespace MonoBall.Core.UI.Windows.Animations.Events
{
    /// <summary>
    /// Event fired when a window animation completes.
    /// </summary>
    public struct WindowAnimationCompletedEvent
    {
        /// <summary>
        /// Gets or sets the animation entity.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// Gets or sets the window entity that was animated.
        /// </summary>
        public Entity WindowEntity { get; set; }
    }
}
```

#### WindowAnimationDestroyEvent

```csharp
namespace MonoBall.Core.UI.Windows.Animations.Events
{
    /// <summary>
    /// Event fired when a window should be destroyed (after animation completes).
    /// </summary>
    public struct WindowAnimationDestroyEvent
    {
        /// <summary>
        /// Gets or sets the animation entity.
        /// </summary>
        public Entity Entity { get; set; }

        /// <summary>
        /// Gets or sets the window entity to destroy.
        /// </summary>
        public Entity WindowEntity { get; set; }
    }
}
```

### Window Renderer Integration

#### Updated WindowRenderer

```csharp
namespace MonoBall.Core.UI.Windows
{
    /// <summary>
    /// Renders a UI window using pluggable border, background, and content renderers.
    /// Supports animation via WindowAnimationComponent.
    /// </summary>
    public class WindowRenderer
    {
        // ... existing code ...

        /// <summary>
        /// Renders the complete window (border, background, content) with optional animation.
        /// </summary>
        /// <param name="spriteBatch">The sprite batch for rendering.</param>
        /// <param name="bounds">The window bounds (coordinates already scaled by caller).</param>
        /// <param name="animation">Optional animation component for animated windows.</param>
        /// <remarks>
        /// SpriteBatch.Begin() must be called before this method.
        /// If animation is provided, position offset, scale, and opacity are applied.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when spriteBatch is null.</exception>
        public void Render(
            SpriteBatch spriteBatch,
            WindowBounds bounds,
            WindowAnimationComponent? animation = null
        )
        {
            if (spriteBatch == null)
            {
                throw new ArgumentNullException(nameof(spriteBatch));
            }

            // Apply animation transformations
            int animatedX = bounds.OuterX;
            int animatedY = bounds.OuterY;
            float animatedScale = 1.0f;
            float animatedOpacity = 1.0f;

            if (animation.HasValue)
            {
                var anim = animation.Value;
                animatedX += (int)anim.PositionOffset.X;
                animatedY += (int)anim.PositionOffset.Y;
                animatedScale = anim.Scale;
                animatedOpacity = anim.Opacity;
            }

            // Calculate animated bounds
            // Scale both outer and interior dimensions independently
            // Note: For non-uniform borders (like MessageBox), the caller must calculate
            // animated bounds manually, as border thicknesses differ per side.
            int animatedOuterWidth = (int)(bounds.OuterWidth * animatedScale);
            int animatedOuterHeight = (int)(bounds.OuterHeight * animatedScale);
            int animatedInteriorWidth = (int)(bounds.InteriorWidth * animatedScale);
            int animatedInteriorHeight = (int)(bounds.InteriorHeight * animatedScale);
            
            // Calculate animated interior position (maintain border offset proportionally)
            // This assumes uniform borders - non-uniform borders need manual calculation
            int borderOffsetX = bounds.InteriorX - bounds.OuterX;
            int borderOffsetY = bounds.InteriorY - bounds.OuterY;
            int animatedInteriorX = animatedX + (int)(borderOffsetX * animatedScale);
            int animatedInteriorY = animatedY + (int)(borderOffsetY * animatedScale);
            
            var animatedBounds = new WindowBounds(
                animatedX,
                animatedY,
                animatedOuterWidth,
                animatedOuterHeight,
                animatedInteriorX,
                animatedInteriorY,
                animatedInteriorWidth,
                animatedInteriorHeight
            );

            // Apply opacity via color tinting (SpriteBatch supports alpha blending)
            Color renderColor = Color.White * animatedOpacity;

            // Render background first (behind border)
            if (_backgroundRenderer != null)
            {
                // Note: Renderers don't currently accept color parameter for opacity
                // Opacity will need to be applied via SpriteBatch.Begin() with BlendState.AlphaBlend
                // and color tinting per draw call, or renderer interfaces need to be updated
                _backgroundRenderer.RenderBackground(
                    spriteBatch,
                    animatedBounds.InteriorX,
                    animatedBounds.InteriorY,
                    animatedBounds.InteriorWidth,
                    animatedBounds.InteriorHeight
                );
            }

            // Render border around interior
            if (_borderRenderer != null)
            {
                _borderRenderer.RenderBorder(
                    spriteBatch,
                    animatedBounds.InteriorX,
                    animatedBounds.InteriorY,
                    animatedBounds.InteriorWidth,
                    animatedBounds.InteriorHeight
                );
            }

            // Render content last (on top)
            if (_contentRenderer != null)
            {
                _contentRenderer.RenderContent(
                    spriteBatch,
                    animatedBounds.InteriorX,
                    animatedBounds.InteriorY,
                    animatedBounds.InteriorWidth,
                    animatedBounds.InteriorHeight
                );
            }
        }
    }
}
```

**Note**: Opacity support requires:
- `SpriteBatch.Begin()` with `BlendState.AlphaBlend` (caller responsibility)
- Color tinting per draw call: `Color.White * opacity`
- Renderer interfaces may need to be updated to accept color parameter for opacity
- Current renderers don't support opacity - this is a future enhancement

## Migration Strategy

### Phase 1: Create Window Animation System

1. Create `WindowAnimationComponent`, `WindowAnimationConfig`, `WindowAnimationPhase`
2. Create `WindowAnimationSystem` with update logic
3. Create animation events (`WindowAnimationStartedEvent`, `WindowAnimationCompletedEvent`, `WindowAnimationDestroyEvent`)
4. Update `WindowRenderer` to support animation parameters

### Phase 2: Migrate Map Popup Animation

1. Create helper method to convert `PopupAnimationComponent` to `WindowAnimationComponent`:

```csharp
public static WindowAnimationComponent FromPopupAnimation(
    PopupAnimationComponent popupAnim,
    Entity windowEntity,
    float windowHeight
)
{
    var phases = new List<WindowAnimationPhase>
    {
        // Slide down phase
        new WindowAnimationPhase
        {
            Type = WindowAnimationType.Slide,
            Duration = popupAnim.SlideDownDuration,
            Easing = WindowEasingType.EaseOutCubic,
            StartValue = new Vector3(0, -windowHeight, 0), // Start above screen
            EndValue = new Vector3(0, 0, 0), // End at visible position
        },
        // Pause phase
        new WindowAnimationPhase
        {
            Type = WindowAnimationType.Pause,
            Duration = popupAnim.PauseDuration,
            Easing = WindowEasingType.Linear,
            StartValue = new Vector3(0, 0, 0),
            EndValue = new Vector3(0, 0, 0),
        },
        // Slide up phase
        new WindowAnimationPhase
        {
            Type = WindowAnimationType.Slide,
            Duration = popupAnim.SlideUpDuration,
            Easing = WindowEasingType.EaseInCubic,
            StartValue = new Vector3(0, 0, 0), // Start at visible position
            EndValue = new Vector3(0, -windowHeight, 0), // End above screen
        },
    };

    return new WindowAnimationComponent
    {
        State = WindowAnimationState.NotStarted,
        ElapsedTime = 0f,
        Config = new WindowAnimationConfig
        {
            Phases = phases,
            WindowSize = new Vector2(0, windowHeight), // Width not needed for slide
            InitialPosition = Vector2.Zero,
            Loop = false,
            DestroyOnComplete = true, // Map popups destroy on complete
        },
        PositionOffset = new Vector2(0, -windowHeight), // Start off-screen
        Scale = 1.0f,
        Opacity = 1.0f,
        WindowEntity = windowEntity,
    };
}
```

2. Update `MapPopupSystem` to create `WindowAnimationComponent` instead of `PopupAnimationComponent`
3. Update `MapPopupRendererSystem` to query for `WindowAnimationComponent` and pass to `WindowRenderer.Render()`
4. Update `MapPopupSystem` to listen for `WindowAnimationCompletedEvent` instead of checking animation state
5. Update `MapPopupSystem` to listen for `WindowAnimationDestroyEvent` to handle cleanup
6. Remove `PopupAnimationComponent` and `PopupAnimationState` enum

### Phase 3: Update Window Renderer Usage

1. Systems that render windows query for `WindowAnimationComponent`
2. Pass animation component to `WindowRenderer.Render()` if present
3. Handle `WindowAnimationDestroyEvent` to clean up windows
4. Validate `WindowEntity` is alive before using animation component

## Usage Examples

### Map Popup Animation (Migrated)

```csharp
// In MapPopupSystem.OnMapPopupShow()
var animationConfig = WindowAnimationHelper.CreateSlideDownUpAnimation(
    slideDownDuration: 0.4f,
    pauseDuration: 2.5f,
    slideUpDuration: 0.4f,
    windowHeight: popupHeight,
    destroyOnComplete: true
);

var windowAnim = new WindowAnimationComponent
{
    State = WindowAnimationState.NotStarted,
    ElapsedTime = 0f,
    Config = animationConfig,
    PositionOffset = new Vector2(0, -popupHeight),
    Scale = 1.0f,
    Opacity = 1.0f,
    WindowEntity = popupEntity,
};

World.Add(popupEntity, windowAnim);
```

### Fade In Animation

```csharp
var fadeInConfig = new WindowAnimationConfig
{
    Phases = new List<WindowAnimationPhase>
    {
        new WindowAnimationPhase
        {
            Type = WindowAnimationType.Fade,
            Duration = 0.3f,
            Easing = WindowEasingType.EaseOut,
            StartValue = new Vector3(0, 0, 0f), // Start transparent
            EndValue = new Vector3(0, 0, 1f), // End opaque
        },
    },
    WindowSize = windowSize,
    InitialPosition = windowPosition,
    Loop = false,
    DestroyOnComplete = false,
};
```

### Slide and Fade Combined

```csharp
var slideFadeConfig = new WindowAnimationConfig
{
    Phases = new List<WindowAnimationPhase>
    {
        new WindowAnimationPhase
        {
            Type = WindowAnimationType.SlideFade,
            Duration = 0.5f,
            Easing = WindowEasingType.EaseOutCubic,
            StartValue = new Vector3(0, -100, 0f), // Start above, transparent
            EndValue = new Vector3(0, 0, 1f), // End at position, opaque
        },
    },
    WindowSize = windowSize,
    InitialPosition = windowPosition,
    Loop = false,
    DestroyOnComplete = false,
};
```

## Helper Classes

### WindowAnimationHelper

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.UI.Windows.Animations
{
    /// <summary>
    /// Helper methods for creating common window animation configurations.
    /// </summary>
    public static class WindowAnimationHelper
    {
        /// <summary>
        /// Creates a slide down → pause → slide up animation (for map popups).
        /// </summary>
        /// <param name="slideDownDuration">Duration of slide down phase in seconds. Must be positive and finite.</param>
        /// <param name="pauseDuration">Duration of pause phase in seconds. Must be positive and finite.</param>
        /// <param name="slideUpDuration">Duration of slide up phase in seconds. Must be positive and finite.</param>
        /// <param name="windowHeight">Height of the window in pixels. Must be positive.</param>
        /// <param name="destroyOnComplete">Whether to destroy the window when animation completes.</param>
        /// <returns>A configured WindowAnimationConfig for slide down/up animation.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when durations or windowHeight are invalid.</exception>
        public static WindowAnimationConfig CreateSlideDownUpAnimation(
            float slideDownDuration,
            float pauseDuration,
            float slideUpDuration,
            float windowHeight,
            bool destroyOnComplete = true
        )
        {
            if (slideDownDuration <= 0f || !float.IsFinite(slideDownDuration))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slideDownDuration),
                    "Slide down duration must be positive and finite."
                );
            }

            if (pauseDuration < 0f || !float.IsFinite(pauseDuration))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(pauseDuration),
                    "Pause duration must be non-negative and finite."
                );
            }

            if (slideUpDuration <= 0f || !float.IsFinite(slideUpDuration))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slideUpDuration),
                    "Slide up duration must be positive and finite."
                );
            }

            if (windowHeight <= 0f || !float.IsFinite(windowHeight))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(windowHeight),
                    "Window height must be positive and finite."
                );
            }
        {
            return new WindowAnimationConfig
            {
                Phases = new List<WindowAnimationPhase>
                {
                    new WindowAnimationPhase
                    {
                        Type = WindowAnimationType.Slide,
                        Duration = slideDownDuration,
                        Easing = WindowEasingType.EaseOutCubic,
                        StartValue = new Vector3(0, -windowHeight, 0),
                        EndValue = new Vector3(0, 0, 0),
                    },
                    new WindowAnimationPhase
                    {
                        Type = WindowAnimationType.Pause,
                        Duration = pauseDuration,
                        Easing = WindowEasingType.Linear,
                        StartValue = new Vector3(0, 0, 0),
                        EndValue = new Vector3(0, 0, 0),
                    },
                    new WindowAnimationPhase
                    {
                        Type = WindowAnimationType.Slide,
                        Duration = slideUpDuration,
                        Easing = WindowEasingType.EaseInCubic,
                        StartValue = new Vector3(0, 0, 0),
                        EndValue = new Vector3(0, -windowHeight, 0),
                    },
                },
                WindowSize = new Vector2(0, windowHeight),
                InitialPosition = Vector2.Zero,
                Loop = false,
                DestroyOnComplete = destroyOnComplete,
            };
        }

        /// <summary>
        /// Creates a fade in animation.
        /// </summary>
        /// <param name="duration">Duration of fade in phase in seconds. Must be positive and finite.</param>
        /// <returns>A configured WindowAnimationConfig for fade in animation.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when duration is invalid.</exception>
        public static WindowAnimationConfig CreateFadeInAnimation(float duration)
        {
            if (duration <= 0f || !float.IsFinite(duration))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(duration),
                    "Duration must be positive and finite."
                );
            }
        {
            return new WindowAnimationConfig
            {
                Phases = new List<WindowAnimationPhase>
                {
                    new WindowAnimationPhase
                    {
                        Type = WindowAnimationType.Fade,
                        Duration = duration,
                        Easing = WindowEasingType.EaseOut,
                        StartValue = new Vector3(0, 0, 0f),
                        EndValue = new Vector3(0, 0, 1f),
                    },
                },
                WindowSize = Vector2.Zero,
                InitialPosition = Vector2.Zero,
                Loop = false,
                DestroyOnComplete = false,
            };
        }
    }
}
```

## File Structure

```
MonoBall.Core/
├── UI/
│   └── Windows/
│       ├── Animations/
│       │   ├── WindowAnimationComponent.cs
│       │   ├── WindowAnimationConfig.cs
│       │   ├── WindowAnimationPhase.cs
│       │   ├── WindowAnimationType.cs
│       │   ├── WindowAnimationState.cs
│       │   ├── WindowEasingType.cs
│       │   ├── WindowAnimationSystem.cs
│       │   ├── WindowAnimationHelper.cs
│       │   └── Events/
│       │       ├── WindowAnimationStartedEvent.cs
│       │       ├── WindowAnimationCompletedEvent.cs
│       │       └── WindowAnimationDestroyEvent.cs
│       └── WindowRenderer.cs (updated)
```

## Notes

### Component Initialization
- **Collections Must Be Initialized**: `WindowAnimationConfig.Phases` must be initialized (non-null) when component is created. Helper methods (`WindowAnimationHelper`) ensure proper initialization.
- **Entity References**: `WindowAnimationComponent.WindowEntity` should be validated (`World.IsAlive`) before use if the window entity may be destroyed.

### Animation Value Field Usage
- **Vector3 Field Usage**: `WindowAnimationPhase.StartValue` and `EndValue` use `Vector3` fields differently based on animation type:
  - `Slide`: X, Y = position offset, Z = unused
  - `Fade`: Z = opacity (0.0-1.0), X, Y = unused
  - `Scale`: Z = scale factor (1.0 = normal), X, Y = unused
  - `SlideFade`: X, Y = position offset, Z = opacity (0.0-1.0)
  - `SlideScale`: X, Y = position offset, Z = scale factor
  - `Pause`: X, Y, Z = unused
  - `None`: X, Y, Z = unused (instant transition)

### Event System
- **Event Firing**: Events are collected during query iteration and fired after query completes to avoid Arch ECS structural change violations.
- **Event-Driven Cleanup**: Systems listen for `WindowAnimationCompletedEvent` and `WindowAnimationDestroyEvent` to handle cleanup.

### Rendering
- **Opacity Support**: Opacity requires `SpriteBatch.Begin()` with `BlendState.AlphaBlend` and color tinting (`Color.White * opacity`). Current renderer interfaces don't support opacity - this is a future enhancement.
- **Scale Support**: Scaling windows requires recalculating both outer and interior bounds. The animation system scales border offsets proportionally, which works for uniform borders. For non-uniform borders (like MessageBox), the caller must calculate animated bounds manually.
- **Position Offset**: Applied in screen space (already scaled coordinates).

### Animation Features
- **Loop Support**: Implemented - animations with `Loop = true` restart from the beginning when they complete.
- **Pause/Resume**: Implemented via `WindowAnimationSystem.PauseAnimation()` and `ResumeAnimation()` methods.
- **Duration Validation**: Phase durations are validated to be positive and finite. Invalid durations are logged and skipped.

### ECS Integration
- **Component-Based**: Animation is component-based, allowing multiple windows to animate independently.
- **Query Performance**: QueryDescription is cached in constructor (Arch ECS best practice).
- **Migration**: `PopupAnimationComponent` can be converted to `WindowAnimationComponent` using helper methods.

