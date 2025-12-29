using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Utilities;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System responsible for handling sprite sheet change requests for entities that support multiple sprite sheets.
///     Currently used by Players, but designed to support NPCs if they need sprite sheet switching in the future.
/// </summary>
public class SpriteSheetSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;

    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the SpriteSheetSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="resourceManager">The resource manager for validating sprite sheets.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public SpriteSheetSystem(World world, IResourceManager resourceManager, ILogger logger)
        : base(world)
    {
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to sprite sheet change requests
        EventBus.Subscribe<SpriteSheetChangeRequestEvent>(OnSpriteSheetChangeRequest);
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
    public int Priority => SystemPriority.SpriteSheet;

    /// <summary>
    ///     Updates the sprite sheet system.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    /// <remarks>
    ///     This system is event-driven and doesn't require per-frame updates.
    /// </remarks>
    public override void Update(in float deltaTime)
    {
        // No per-frame logic needed - system is event-driven
    }

    /// <summary>
    ///     Handles sprite sheet change requests for entities with SpriteSheetComponent.
    /// </summary>
    /// <param name="evt">The sprite sheet change request event.</param>
    private void OnSpriteSheetChangeRequest(SpriteSheetChangeRequestEvent evt)
    {
        // Validate entity has SpriteSheetComponent (required for sprite sheet switching)
        if (!World.Has<SpriteSheetComponent>(evt.Entity))
        {
            _logger.Warning(
                "SpriteSheetSystem.OnSpriteSheetChangeRequest: Entity {EntityId} does not have SpriteSheetComponent",
                evt.Entity.Id
            );
            return;
        }

        // Validate sprite sheet and animation exist using helper
        // Use forgiving validation (log warning, return) rather than throwing
        // This allows the system to gracefully handle invalid requests without crashing
        var entityType = DetermineEntityType(evt.Entity);
        if (
            !SpriteValidationHelper.ValidateSpriteAndAnimation(
                _resourceManager,
                _logger,
                evt.NewSpriteSheetId,
                evt.AnimationName,
                entityType,
                evt.Entity.Id.ToString(),
                false
            )
        )
            // Validation failed (already logged by helper), skip sprite sheet change
            return;

        // Get current sprite sheet ID
        ref var spriteSheet = ref World.Get<SpriteSheetComponent>(evt.Entity);
        var oldSpriteSheetId = spriteSheet.CurrentSpriteSheetId;

        // Update sprite sheet
        spriteSheet.CurrentSpriteSheetId = evt.NewSpriteSheetId;

        // Update animation component if it exists
        if (!World.Has<SpriteAnimationComponent>(evt.Entity))
        {
            _logger.Warning(
                "SpriteSheetSystem.OnSpriteSheetChangeRequest: Entity {EntityId} does not have SpriteAnimationComponent",
                evt.Entity.Id
            );
            return;
        }

        ref var anim = ref World.Get<SpriteAnimationComponent>(evt.Entity);
        anim.CurrentAnimationName = evt.AnimationName;
        anim.CurrentFrameIndex = 0;
        anim.ElapsedTime = 0.0f;

        // Publish sprite sheet changed event
        var changedEvent = new SpriteSheetChangedEvent
        {
            Entity = evt.Entity,
            OldSpriteSheetId = oldSpriteSheetId,
            NewSpriteSheetId = evt.NewSpriteSheetId,
        };
        EventBus.Send(ref changedEvent);

        _logger.Information(
            "SpriteSheetSystem.OnSpriteSheetChangeRequest: Changed sprite sheet for {EntityType} entity {EntityId} from {OldSpriteSheetId} to {NewSpriteSheetId} with animation {AnimationName}",
            entityType,
            evt.Entity.Id,
            oldSpriteSheetId,
            evt.NewSpriteSheetId,
            evt.AnimationName
        );
    }

    /// <summary>
    ///     Determines the entity type for logging purposes.
    /// </summary>
    /// <param name="entity">The entity to check.</param>
    /// <returns>The entity type string ("Player", "NPC", or "Entity").</returns>
    private string DetermineEntityType(Entity entity)
    {
        if (World.Has<PlayerComponent>(entity))
            return "Player";

        if (World.Has<NpcComponent>(entity))
            return "NPC";

        return "Entity";
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
                EventBus.Unsubscribe<SpriteSheetChangeRequestEvent>(OnSpriteSheetChangeRequest);
            _disposed = true;
        }
    }
}
