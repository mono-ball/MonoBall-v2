using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Maps;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for updating animation timers and advancing frames for NPC sprites.
    /// </summary>
    public class NpcAnimationSystem : BaseSystem<World, float>, IDisposable
    {
        private readonly ISpriteLoaderService _spriteLoader;
        private readonly QueryDescription _queryDescription;

        /// <summary>
        /// Initializes a new instance of the NpcAnimationSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="spriteLoader">The sprite loader service for accessing animation frame cache.</param>
        public NpcAnimationSystem(World world, ISpriteLoaderService spriteLoader)
            : base(world)
        {
            _spriteLoader = spriteLoader ?? throw new ArgumentNullException(nameof(spriteLoader));
            _queryDescription = new QueryDescription().WithAll<
                NpcComponent,
                SpriteAnimationComponent
            >();

            // Subscribe to animation change events to reset animation state
            EventBus.Subscribe<NpcAnimationChangedEvent>(OnAnimationChanged);
        }

        /// <summary>
        /// Updates animation timers and advances frames for all NPCs.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime; // Copy to avoid ref parameter in lambda
            World.Query(
                in _queryDescription,
                (ref NpcComponent npc, ref SpriteAnimationComponent anim) =>
                {
                    UpdateAnimation(ref npc, ref anim, dt);
                }
            );
        }

        /// <summary>
        /// Updates animation timer and advances frame for a single NPC.
        /// </summary>
        /// <param name="npc">The NPC component.</param>
        /// <param name="anim">The sprite animation component.</param>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        private void UpdateAnimation(
            ref NpcComponent npc,
            ref SpriteAnimationComponent anim,
            float deltaTime
        )
        {
            // Get animation frames from cache
            var frames = _spriteLoader.GetAnimationFrames(npc.SpriteId, anim.CurrentAnimationName);

            if (frames == null || frames.Count == 0)
            {
                Log.Warning(
                    "NpcAnimationSystem.UpdateAnimation: Animation frames not found for sprite {SpriteId}, animation {AnimationName}",
                    npc.SpriteId,
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

                // Advance to next frame (loop to 0 when reaching end)
                anim.CurrentFrameIndex++;
                if (anim.CurrentFrameIndex >= frameCount)
                {
                    anim.CurrentFrameIndex = 0;
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
        private void OnAnimationChanged(NpcAnimationChangedEvent evt)
        {
            if (World.Has<SpriteAnimationComponent>(evt.NpcEntity))
            {
                ref var anim = ref World.Get<SpriteAnimationComponent>(evt.NpcEntity);
                anim.CurrentFrameIndex = 0;
                anim.ElapsedTime = 0.0f;
            }
        }

        private bool _disposed = false;

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <remarks>
        /// This system implements IDisposable to properly clean up event subscriptions.
        /// The base BaseSystem class may or may not implement IDisposable, but this ensures
        /// proper cleanup of managed resources (event subscriptions).
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
                    EventBus.Unsubscribe<NpcAnimationChangedEvent>(OnAnimationChanged);
                }
                _disposed = true;
            }
        }
    }
}
