using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Maps;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for updating animation timers and advancing frames for sprite animations (NPCs and Players).
    /// </summary>
    public class SpriteAnimationSystem : BaseSystem<World, float>, IDisposable
    {
        private readonly ISpriteLoaderService _spriteLoader;
        private readonly QueryDescription _npcQuery;
        private readonly QueryDescription _playerQuery;

        // Track previous animation names to detect changes
        private readonly Dictionary<Entity, string> _previousAnimationNames =
            new Dictionary<Entity, string>();

        // Reusable collections for cleanup to avoid allocations in hot paths
        private readonly HashSet<Entity> _entitiesThisFrame = new HashSet<Entity>();
        private readonly List<Entity> _keysToRemove = new List<Entity>();

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the SpriteAnimationSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="spriteLoader">The sprite loader service for accessing animation frame cache.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public SpriteAnimationSystem(World world, ISpriteLoaderService spriteLoader, ILogger logger)
            : base(world)
        {
            _spriteLoader = spriteLoader ?? throw new ArgumentNullException(nameof(spriteLoader));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Separate queries for NPCs and Players (avoid World.Has<> checks in hot path)
            _npcQuery = new QueryDescription().WithAll<NpcComponent, SpriteAnimationComponent>();

            _playerQuery = new QueryDescription().WithAll<
                PlayerComponent,
                SpriteSheetComponent,
                SpriteAnimationComponent
            >();

            // Subscribe to animation change events to reset animation state
            EventBus.Subscribe<SpriteAnimationChangedEvent>(OnAnimationChanged);
        }

        /// <summary>
        /// Updates animation timers and advances frames for all sprites (NPCs and Players).
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime; // Copy to avoid ref parameter in lambda

            // Clear and reuse collections for tracking entities this frame
            _entitiesThisFrame.Clear();
            _keysToRemove.Clear();

            // Update NPC animations
            World.Query(
                in _npcQuery,
                (Entity entity, ref NpcComponent npc, ref SpriteAnimationComponent anim) =>
                {
                    _entitiesThisFrame.Add(entity);
                    string previousAnimationName = GetPreviousAnimationName(
                        entity,
                        anim.CurrentAnimationName
                    );
                    bool animationLoops = _spriteLoader.GetAnimationLoops(
                        npc.SpriteId,
                        anim.CurrentAnimationName
                    );

                    // Set FlipHorizontal from animation manifest (matches oldmonoball line 130)
                    anim.FlipHorizontal = _spriteLoader.GetAnimationFlipHorizontal(
                        npc.SpriteId,
                        anim.CurrentAnimationName
                    );

                    UpdateAnimation(entity, npc.SpriteId, ref anim, dt, animationLoops);
                    CheckAndPublishAnimationChange(
                        entity,
                        previousAnimationName,
                        anim.CurrentAnimationName
                    );
                }
            );

            // Update Player animations
            World.Query(
                in _playerQuery,
                (
                    Entity entity,
                    ref PlayerComponent player,
                    ref SpriteSheetComponent spriteSheet,
                    ref SpriteAnimationComponent anim
                ) =>
                {
                    _entitiesThisFrame.Add(entity);
                    string previousAnimationName = GetPreviousAnimationName(
                        entity,
                        anim.CurrentAnimationName
                    );
                    bool animationLoops = _spriteLoader.GetAnimationLoops(
                        spriteSheet.CurrentSpriteSheetId,
                        anim.CurrentAnimationName
                    );

                    // Set FlipHorizontal from animation manifest (matches oldmonoball line 130)
                    anim.FlipHorizontal = _spriteLoader.GetAnimationFlipHorizontal(
                        spriteSheet.CurrentSpriteSheetId,
                        anim.CurrentAnimationName
                    );

                    UpdateAnimation(
                        entity,
                        spriteSheet.CurrentSpriteSheetId,
                        ref anim,
                        dt,
                        animationLoops
                    );
                    CheckAndPublishAnimationChange(
                        entity,
                        previousAnimationName,
                        anim.CurrentAnimationName
                    );
                }
            );

            // Clean up dictionary entries for entities that no longer exist
            // This prevents memory leaks when entities are destroyed
            foreach (var kvp in _previousAnimationNames)
            {
                if (!_entitiesThisFrame.Contains(kvp.Key))
                {
                    _keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in _keysToRemove)
            {
                _previousAnimationNames.Remove(key);
            }
        }

        /// <summary>
        /// Gets the previous animation name for an entity, or returns the current one if not tracked.
        /// </summary>
        private string GetPreviousAnimationName(Entity entity, string currentAnimationName)
        {
            if (_previousAnimationNames.TryGetValue(entity, out string? previousName))
            {
                return previousName;
            }

            // First time seeing this entity, store current animation as previous
            _previousAnimationNames[entity] = currentAnimationName;
            return currentAnimationName;
        }

        /// <summary>
        /// Checks if animation name changed and publishes event if it did.
        /// </summary>
        private void CheckAndPublishAnimationChange(
            Entity entity,
            string previousAnimationName,
            string currentAnimationName
        )
        {
            if (previousAnimationName != currentAnimationName)
            {
                // Update stored previous animation name
                _previousAnimationNames[entity] = currentAnimationName;

                // Publish event
                var evt = new SpriteAnimationChangedEvent
                {
                    Entity = entity,
                    OldAnimationName = previousAnimationName,
                    NewAnimationName = currentAnimationName,
                };
                EventBus.Send(ref evt);
            }
        }

        /// <summary>
        /// Updates animation timer and advances frame for a single sprite.
        /// Handles PlayOnce mode and sets IsComplete when animation finishes.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="spriteId">The sprite ID.</param>
        /// <param name="anim">The sprite animation component.</param>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        /// <param name="animationLoops">Whether the animation definition has Loop=true.</param>
        private void UpdateAnimation(
            Entity entity,
            string spriteId,
            ref SpriteAnimationComponent anim,
            float deltaTime,
            bool animationLoops
        )
        {
            // Skip if not playing
            if (!anim.IsPlaying)
            {
                return;
            }

            // Get animation frames from cache
            var frames = _spriteLoader.GetAnimationFrames(spriteId, anim.CurrentAnimationName);

            if (frames == null || frames.Count == 0)
            {
                Log.Warning(
                    "SpriteAnimationSystem.UpdateAnimation: Animation frames not found for sprite {SpriteId}, animation {AnimationName}",
                    spriteId,
                    anim.CurrentAnimationName
                );
                return;
            }

            // Update elapsed time
            anim.ElapsedTime += deltaTime;

            // Cache frame count for bounds checking
            int frameCount = frames.Count;
            if (frameCount == 0)
            {
                return;
            }

            // Ensure current frame index is within bounds
            if (anim.CurrentFrameIndex < 0 || anim.CurrentFrameIndex >= frameCount)
            {
                anim.CurrentFrameIndex = 0;
            }

            // Get current frame duration in seconds
            float frameDurationSeconds = frames[anim.CurrentFrameIndex].DurationSeconds;

            // Advance to next frame if duration exceeded
            while (anim.ElapsedTime >= frameDurationSeconds && frameCount > 0)
            {
                // Subtract frame duration for frame-perfect timing
                anim.ElapsedTime -= frameDurationSeconds;

                // Advance to next frame
                anim.CurrentFrameIndex++;

                // Handle end of animation sequence
                if (anim.CurrentFrameIndex >= frameCount)
                {
                    // PlayOnce overrides Loop setting - treat as non-looping
                    if (animationLoops && !anim.PlayOnce)
                    {
                        // Animation loops - reset to first frame
                        anim.CurrentFrameIndex = 0;
                        anim.TriggeredEventFrames = 0; // Reset event triggers on loop
                    }
                    else
                    {
                        // Non-looping animation completed (or PlayOnce completed one cycle)
                        anim.CurrentFrameIndex = frameCount - 1;
                        anim.IsComplete = true;
                        anim.IsPlaying = false;
                        // Don't advance further - animation is done
                        break;
                    }
                }

                // Defensive check: ensure index is still valid before accessing
                if (anim.CurrentFrameIndex < 0 || anim.CurrentFrameIndex >= frameCount)
                {
                    anim.CurrentFrameIndex = 0;
                }

                // Get new frame duration
                frameDurationSeconds = frames[anim.CurrentFrameIndex].DurationSeconds;
            }
        }

        /// <summary>
        /// Handles animation change events by resetting animation state.
        /// </summary>
        /// <param name="evt">The animation changed event.</param>
        private void OnAnimationChanged(SpriteAnimationChangedEvent evt)
        {
            if (World.Has<SpriteAnimationComponent>(evt.Entity))
            {
                ref var anim = ref World.Get<SpriteAnimationComponent>(evt.Entity);
                anim.CurrentFrameIndex = 0;
                anim.ElapsedTime = 0.0f;
                anim.IsPlaying = true;
                anim.IsComplete = false;
                anim.TriggeredEventFrames = 0;
                // NOTE: PlayOnce should be set by the system that changes the animation (e.g., MovementSystem), not here

                // Update stored previous animation name
                _previousAnimationNames[evt.Entity] = evt.NewAnimationName;
            }
            else
            {
                // Entity no longer exists, clean up dictionary entry to prevent memory leak
                _previousAnimationNames.Remove(evt.Entity);
            }
        }

        private bool _disposed = false;

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <remarks>
        /// Implements IDisposable to properly clean up event subscriptions.
        /// Uses standard dispose pattern without finalizer since only managed resources are disposed.
        /// Uses 'new' keyword because BaseSystem may have a Dispose() method with different signature.
        /// </remarks>
        public new void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    EventBus.Unsubscribe<SpriteAnimationChangedEvent>(OnAnimationChanged);
                    _previousAnimationNames.Clear();
                }
                _disposed = true;
            }
        }
    }
}
