using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Maps;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System responsible for updating animation timers and advancing frames for sprite animations (NPCs and Players).
/// </summary>
public class SpriteAnimationSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    // Reusable collections for cleanup to avoid allocations in hot paths
    private readonly HashSet<Entity> _entitiesThisFrame = new();
    private readonly List<Entity> _keysToRemove = new();

    private readonly ILogger _logger;
    private readonly QueryDescription _npcQuery;
    private readonly QueryDescription _playerQuery;

    // Track previous animation names to detect changes
    private readonly Dictionary<Entity, string> _previousAnimationNames = new();

    private readonly IResourceManager _resourceManager;
    private readonly List<IDisposable> _subscriptions = new();

    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the SpriteAnimationSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="resourceManager">The resource manager for accessing animation frame cache.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public SpriteAnimationSystem(World world, IResourceManager resourceManager, ILogger logger)
        : base(world)
    {
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Separate queries for NPCs and Players (avoid World.Has<> checks in hot path)
        // NPC query includes ActiveMapEntity tag to only process NPCs in active maps
        // Both queries require SpriteComponent (sprite data) and SpriteAnimationComponent (animation state)
        _npcQuery = new QueryDescription().WithAll<
            NpcComponent,
            SpriteComponent,
            SpriteAnimationComponent,
            ActiveMapEntity
        >();

        _playerQuery = new QueryDescription().WithAll<
            PlayerComponent,
            SpriteSheetComponent,
            SpriteComponent,
            SpriteAnimationComponent
        >();

        // Subscribe to animation change events to reset animation state
        _subscriptions.Add(EventBus.Subscribe<SpriteAnimationChangedEvent>(OnAnimationChanged));
    }

    /// <summary>
    ///     Disposes the system and unsubscribes from events.
    /// </summary>
    /// <remarks>
    ///     Implements IDisposable to properly clean up event subscriptions.
    ///     Uses standard dispose pattern without finalizer since only managed resources are disposed.
    ///     Uses 'new' keyword because BaseSystem may have a Dispose() method with different signature.
    /// </remarks>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.SpriteAnimation;

    /// <summary>
    ///     Updates animation timers and advances frames for all sprites (NPCs and Players).
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
    public override void Update(in float deltaTime)
    {
        var dt = deltaTime; // Copy to avoid ref parameter in lambda

        // Clear and reuse collections for tracking entities this frame
        _entitiesThisFrame.Clear();
        _keysToRemove.Clear();

        // Update NPC animations
        World.Query(
            in _npcQuery,
            (
                Entity entity,
                ref NpcComponent npc,
                ref SpriteComponent sprite,
                ref SpriteAnimationComponent anim
            ) =>
            {
                _entitiesThisFrame.Add(entity);
                UpdateEntityAnimation(entity, sprite.SpriteId, ref sprite, ref anim, dt);
            }
        );

        // Update Player animations
        World.Query(
            in _playerQuery,
            (
                Entity entity,
                ref PlayerComponent player,
                ref SpriteSheetComponent spriteSheet,
                ref SpriteComponent sprite,
                ref SpriteAnimationComponent anim
            ) =>
            {
                _entitiesThisFrame.Add(entity);

                // Sync SpriteComponent.SpriteId with SpriteSheetComponent.CurrentSpriteSheetId for players
                // This ensures they stay in sync if SpriteSheetSystem updates SpriteSheetComponent
                if (sprite.SpriteId != spriteSheet.CurrentSpriteSheetId)
                {
                    _logger.Warning(
                        "SpriteAnimationSystem.Update: SpriteComponent.SpriteId ({SpriteId}) != SpriteSheetComponent.CurrentSpriteSheetId ({SheetId}) for entity {EntityId}. Syncing.",
                        sprite.SpriteId,
                        spriteSheet.CurrentSpriteSheetId,
                        entity.Id
                    );
                    sprite.SpriteId = spriteSheet.CurrentSpriteSheetId;
                }

                UpdateEntityAnimation(
                    entity,
                    spriteSheet.CurrentSpriteSheetId,
                    ref sprite,
                    ref anim,
                    dt
                );
            }
        );

        // Clean up dictionary entries for entities that no longer exist
        // This prevents memory leaks when entities are destroyed
        foreach (var kvp in _previousAnimationNames)
            if (!_entitiesThisFrame.Contains(kvp.Key))
                _keysToRemove.Add(kvp.Key);

        foreach (var key in _keysToRemove)
            _previousAnimationNames.Remove(key);
    }

    /// <summary>
    ///     Updates animation timing and SpriteComponent for a single entity.
    ///     Common logic shared between NPC and Player update paths.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="spriteId">The sprite ID to use for animation lookup.</param>
    /// <param name="sprite">The sprite component to update.</param>
    /// <param name="anim">The animation component.</param>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    private void UpdateEntityAnimation(
        Entity entity,
        string spriteId,
        ref SpriteComponent sprite,
        ref SpriteAnimationComponent anim,
        float deltaTime
    )
    {
        var previousAnimationName = GetPreviousAnimationName(entity, anim.CurrentAnimationName);
        var animationLoops = _resourceManager.GetAnimationLoops(
            spriteId,
            anim.CurrentAnimationName
        );

        // Get animation frames from cache
        var frames = _resourceManager.GetAnimationFrames(spriteId, anim.CurrentAnimationName);

        // Update animation timing (existing logic)
        UpdateAnimation(entity, spriteId, ref anim, deltaTime, animationLoops);

        // Update SpriteComponent based on animation state
        UpdateSpriteFromAnimation(entity, spriteId, ref sprite, ref anim, frames);

        CheckAndPublishAnimationChange(entity, previousAnimationName, anim.CurrentAnimationName);
    }

    /// <summary>
    ///     Gets the previous animation name for an entity, or returns the current one if not tracked.
    /// </summary>
    private string GetPreviousAnimationName(Entity entity, string currentAnimationName)
    {
        if (_previousAnimationNames.TryGetValue(entity, out var previousName))
            return previousName;

        // First time seeing this entity, store current animation as previous
        _previousAnimationNames[entity] = currentAnimationName;
        return currentAnimationName;
    }

    /// <summary>
    ///     Checks if animation name changed and publishes event if it did.
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
    ///     Updates animation timer and advances frame for a single sprite.
    ///     Handles PlayOnce mode and sets IsComplete when animation finishes.
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
            return;

        // Get animation frames from cache
        var frames = _resourceManager.GetAnimationFrames(spriteId, anim.CurrentAnimationName);

        if (frames == null || frames.Count == 0)
        {
            _logger.Warning(
                "SpriteAnimationSystem.UpdateAnimation: Animation frames not found for sprite {SpriteId}, animation {AnimationName}",
                spriteId,
                anim.CurrentAnimationName
            );
            return;
        }

        // Update elapsed time
        anim.ElapsedTime += deltaTime;

        // Cache frame count for bounds checking
        var frameCount = frames.Count;
        if (frameCount == 0)
            return;

        // Ensure current frame index is within bounds
        if (anim.CurrentAnimationFrameIndex < 0 || anim.CurrentAnimationFrameIndex >= frameCount)
            anim.CurrentAnimationFrameIndex = 0;

        // Get current frame duration in seconds
        var frameDurationSeconds = frames[anim.CurrentAnimationFrameIndex].DurationSeconds;

        // Advance to next frame if duration exceeded
        while (anim.ElapsedTime >= frameDurationSeconds && frameCount > 0)
        {
            // Subtract frame duration for frame-perfect timing
            anim.ElapsedTime -= frameDurationSeconds;

            // Advance to next frame
            anim.CurrentAnimationFrameIndex++;

            // Handle end of animation sequence
            if (anim.CurrentAnimationFrameIndex >= frameCount)
            {
                // PlayOnce overrides Loop setting - treat as non-looping
                if (animationLoops && !anim.PlayOnce)
                {
                    // Animation loops - reset to first frame
                    anim.CurrentAnimationFrameIndex = 0;
                    anim.TriggeredEventFrames = 0; // Reset event triggers on loop
                }
                else
                {
                    // Non-looping animation completed (or PlayOnce completed one cycle)
                    anim.CurrentAnimationFrameIndex = frameCount - 1;
                    anim.IsComplete = true;
                    anim.IsPlaying = false;
                    // Don't advance further - animation is done
                    break;
                }
            }

            // Defensive check: ensure index is still valid before accessing
            if (
                anim.CurrentAnimationFrameIndex < 0
                || anim.CurrentAnimationFrameIndex >= frameCount
            )
                anim.CurrentAnimationFrameIndex = 0;

            // Get new frame duration
            frameDurationSeconds = frames[anim.CurrentAnimationFrameIndex].DurationSeconds;
        }
    }

    /// <summary>
    ///     Handles animation change events by resetting animation state and updating SpriteComponent immediately.
    /// </summary>
    /// <param name="evt">The animation changed event.</param>
    private void OnAnimationChanged(SpriteAnimationChangedEvent evt)
    {
        if (World.Has<SpriteAnimationComponent>(evt.Entity))
        {
            ref var anim = ref World.Get<SpriteAnimationComponent>(evt.Entity);
            anim.CurrentAnimationFrameIndex = 0;
            anim.ElapsedTime = 0.0f;
            anim.IsPlaying = true;
            anim.IsComplete = false;
            anim.TriggeredEventFrames = 0;
            // NOTE: PlayOnce should be set by the system that changes the animation (e.g., MovementSystem), not here

            // Update SpriteComponent immediately to prevent one-frame visual glitch
            if (World.Has<SpriteComponent>(evt.Entity))
            {
                ref var sprite = ref World.Get<SpriteComponent>(evt.Entity);

                // Get sprite ID (from SpriteSheetComponent for players, SpriteComponent for NPCs)
                string spriteId = sprite.SpriteId;
                if (World.Has<SpriteSheetComponent>(evt.Entity))
                {
                    ref var spriteSheet = ref World.Get<SpriteSheetComponent>(evt.Entity);
                    spriteId = spriteSheet.CurrentSpriteSheetId;
                    // Sync SpriteComponent.SpriteId with SpriteSheetComponent
                    if (sprite.SpriteId != spriteId)
                    {
                        sprite.SpriteId = spriteId;
                    }
                }

                // Get animation frames for new animation
                var frames = _resourceManager.GetAnimationFrames(spriteId, evt.NewAnimationName);
                if (frames != null && frames.Count > 0)
                {
                    // Update to first frame of new animation
                    sprite.FrameIndex = frames[0].FrameIndex;

                    // Update flip flags immediately
                    sprite.FlipHorizontal = _resourceManager.GetAnimationFlipHorizontal(
                        spriteId,
                        evt.NewAnimationName
                    );
                    sprite.FlipVertical = _resourceManager.GetAnimationFlipVertical(
                        spriteId,
                        evt.NewAnimationName
                    );
                }
            }

            // Update stored previous animation name
            _previousAnimationNames[evt.Entity] = evt.NewAnimationName;
        }
        else
        {
            // Entity no longer exists, clean up dictionary entry to prevent memory leak
            _previousAnimationNames.Remove(evt.Entity);
        }
    }

    /// <summary>
    ///     Disposes the system and unsubscribes from events.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                foreach (var subscription in _subscriptions)
                    subscription.Dispose();
                _previousAnimationNames.Clear();
            }

            _disposed = true;
        }
    }

    /// <summary>
    ///     Updates SpriteComponent based on current animation state.
    ///     Maps animation frame index to sprite frame index and updates flip flags.
    /// </summary>
    /// <param name="entity">The entity being updated.</param>
    /// <param name="spriteId">The sprite ID.</param>
    /// <param name="sprite">The sprite component to update.</param>
    /// <param name="anim">The animation component.</param>
    /// <param name="frames">The precomputed animation frames.</param>
    private void UpdateSpriteFromAnimation(
        Entity entity,
        string spriteId,
        ref SpriteComponent sprite,
        ref SpriteAnimationComponent anim,
        IReadOnlyList<SpriteAnimationFrame> frames
    )
    {
        if (frames == null || frames.Count == 0)
            return;

        // Validate animation frame index is within bounds and reset if invalid
        if (anim.CurrentAnimationFrameIndex < 0 || anim.CurrentAnimationFrameIndex >= frames.Count)
        {
            _logger.Warning(
                "SpriteAnimationSystem.UpdateSpriteFromAnimation: Animation frame index {FrameIndex} out of range for animation {AnimationName} (frame count: {FrameCount}). Resetting to 0.",
                anim.CurrentAnimationFrameIndex,
                anim.CurrentAnimationName,
                frames.Count
            );
            anim.CurrentAnimationFrameIndex = 0;
            // Continue to update SpriteComponent with first frame instead of returning early
        }

        // Get current animation frame (guaranteed to be valid after bounds check/reset)
        var animationFrame = frames[anim.CurrentAnimationFrameIndex];

        // Update SpriteComponent with sprite frame index (O(1) - frame index stored during precomputation)
        // SpriteAnimationFrame.FrameIndex is set during ResourceManager.PrecomputeAnimationFrames()
        sprite.FrameIndex = animationFrame.FrameIndex;

        // Update flip flags from animation manifest
        sprite.FlipHorizontal = _resourceManager.GetAnimationFlipHorizontal(
            spriteId,
            anim.CurrentAnimationName
        );
        sprite.FlipVertical = _resourceManager.GetAnimationFlipVertical(
            spriteId,
            anim.CurrentAnimationName
        );
    }
}
