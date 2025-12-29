using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System responsible for updating camera positions, following targets, and enforcing bounds.
/// </summary>
public class CameraSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    private readonly ILogger _logger;
    private readonly QueryDescription _queryDescription;
    private readonly IResourceManager _resourceManager;

    /// <summary>
    ///     Initializes a new instance of the CameraSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="resourceManager">Resource manager required for calculating sprite centers when following entities.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public CameraSystem(World world, IResourceManager resourceManager, ILogger logger)
        : base(world)
    {
        _queryDescription = new QueryDescription().WithAll<CameraComponent>();
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.Camera;

    /// <summary>
    ///     Updates camera positions, following logic, and bounds enforcement.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        var dt = deltaTime; // Copy to avoid ref parameter in lambda
        World.Query(
            in _queryDescription,
            (ref CameraComponent camera) =>
            {
                UpdateCamera(ref camera, dt);
            }
        );
    }

    /// <summary>
    ///     Calculates the center point of an entity's sprite for camera centering.
    ///     Requires the entity to have SpriteSheetComponent and SpriteAnimationComponent.
    /// </summary>
    /// <param name="entity">The entity to calculate center for.</param>
    /// <param name="position">The entity's position (typically top-left of sprite).</param>
    /// <returns>The center point of the entity's sprite in pixel coordinates.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if entity lacks required components or frame rectangle cannot be
    ///     retrieved.
    /// </exception>
    private Vector2 CalculateEntityCenter(Entity entity, Vector2 position)
    {
        if (!World.Has<SpriteSheetComponent>(entity))
            throw new InvalidOperationException(
                $"CameraSystem.CalculateEntityCenter: Entity {entity.Id} does not have SpriteSheetComponent. "
                    + "Cannot calculate sprite center without sprite sheet information."
            );

        if (!World.Has<SpriteAnimationComponent>(entity))
            throw new InvalidOperationException(
                $"CameraSystem.CalculateEntityCenter: Entity {entity.Id} does not have SpriteAnimationComponent. "
                    + "Cannot calculate sprite center without animation information."
            );

        ref var spriteSheet = ref World.Get<SpriteSheetComponent>(entity);
        ref var animation = ref World.Get<SpriteAnimationComponent>(entity);

        // Get current frame rectangle - will throw if not found (fail-fast)
        Rectangle frameRect;
        try
        {
            frameRect = _resourceManager.GetAnimationFrameRectangle(
                spriteSheet.CurrentSpriteSheetId,
                animation.CurrentAnimationName,
                animation.CurrentFrameIndex
            );
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"CameraSystem.CalculateEntityCenter: Failed to get frame rectangle for entity {entity.Id}, "
                    + $"sprite {spriteSheet.CurrentSpriteSheetId}, animation {animation.CurrentAnimationName}, frame {animation.CurrentFrameIndex}.",
                ex
            );
        }

        // Calculate center: position (top-left) + half frame dimensions
        return new Vector2(position.X + frameRect.Width / 2f, position.Y + frameRect.Height / 2f);
    }

    /// <summary>
    ///     Converts a pixel position to tile coordinates using the camera's tile dimensions.
    /// </summary>
    /// <param name="pixelPosition">The position in pixels.</param>
    /// <param name="tileWidth">The tile width in pixels.</param>
    /// <param name="tileHeight">The tile height in pixels.</param>
    /// <returns>The position in tile coordinates.</returns>
    /// <exception cref="ArgumentException">Thrown if tileWidth or tileHeight is less than or equal to zero.</exception>
    private static Vector2 ConvertPixelToTile(Vector2 pixelPosition, int tileWidth, int tileHeight)
    {
        if (tileWidth <= 0)
            throw new ArgumentException("Tile width must be greater than zero.", nameof(tileWidth));

        if (tileHeight <= 0)
            throw new ArgumentException(
                "Tile height must be greater than zero.",
                nameof(tileHeight)
            );

        return new Vector2(pixelPosition.X / tileWidth, pixelPosition.Y / tileHeight);
    }

    /// <summary>
    ///     Updates a single camera.
    /// </summary>
    private void UpdateCamera(ref CameraComponent camera, float deltaTime)
    {
        if (!camera.IsActive)
            return;

        // Handle entity-based following (takes precedence over position-based following)
        if (camera.FollowEntity.HasValue && !camera.IsFollowingLocked)
        {
            var followEntity = camera.FollowEntity.Value;

            // Validate entity still exists and has required components
            if (!World.Has<PositionComponent>(followEntity))
            {
                // Entity destroyed or missing PositionComponent - clear follow
                camera.FollowEntity = null;
                _logger.Warning(
                    "CameraSystem.UpdateCamera: Follow entity {EntityId} missing PositionComponent (entity may be destroyed), stopping follow",
                    followEntity.Id
                );
            }
            else if (
                !World.Has<SpriteSheetComponent>(followEntity)
                || !World.Has<SpriteAnimationComponent>(followEntity)
            )
            {
                // Entity missing required sprite components - clear follow
                camera.FollowEntity = null;
                _logger.Warning(
                    "CameraSystem.UpdateCamera: Follow entity {EntityId} missing SpriteSheetComponent or SpriteAnimationComponent, stopping follow",
                    followEntity.Id
                );
            }
            else
            {
                // Safe to access components
                ref var targetPos = ref World.Get<PositionComponent>(followEntity);

                // Calculate the center point of the entity's sprite for proper camera centering
                var entityCenter = CalculateEntityCenter(followEntity, targetPos.Position);

                // Convert pixel position to tile coordinates using camera's tile dimensions
                var tilePos = ConvertPixelToTile(entityCenter, camera.TileWidth, camera.TileHeight);

                // Set camera position directly (instant follow, no smoothing)
                // Map bounds clamping is applied after all following logic (see end of method)
                camera.Position = tilePos;
                camera.IsDirty = true;
            }
        }
        // Handle position-based following (only if entity following is not active)
        else if (camera.FollowTarget.HasValue)
        {
            var targetPos = camera.FollowTarget.Value;

            // Apply smoothing if enabled
            if (camera.SmoothingSpeed > 0 && camera.SmoothingSpeed < 1)
                camera.Position = Vector2.Lerp(camera.Position, targetPos, camera.SmoothingSpeed);
            else
                camera.Position = targetPos;

            camera.IsDirty = true;
        }

        // Enforce map bounds
        if (camera.MapBounds != Rectangle.Empty)
            camera.Position = camera.ClampPositionToMapBounds(camera.Position);
    }

    /// <summary>
    ///     Sets the camera position.
    /// </summary>
    /// <param name="cameraEntity">The camera entity.</param>
    /// <param name="position">The new position in tile coordinates.</param>
    /// <param name="clampToBounds">
    ///     Whether to clamp the position to map bounds. Set to false for cutscenes or transitions that
    ///     need to position outside bounds.
    /// </param>
    public void SetCameraPosition(Entity cameraEntity, Vector2 position, bool clampToBounds)
    {
        if (!World.IsAlive(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.SetCameraPosition: Entity {EntityId} is not alive",
                cameraEntity.Id
            );
            return;
        }

        if (!World.Has<CameraComponent>(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.SetCameraPosition: Entity {EntityId} does not have CameraComponent",
                cameraEntity.Id
            );
            return;
        }

        ref var camera = ref World.Get<CameraComponent>(cameraEntity);

        // Apply map bounds clamping if enabled and bounds are set
        if (clampToBounds && camera.MapBounds != Rectangle.Empty)
            position = camera.ClampPositionToMapBounds(position);

        camera.Position = position;
        camera.IsDirty = true;
    }

    /// <summary>
    ///     Sets the camera to follow a target position (in tile coordinates).
    /// </summary>
    /// <param name="cameraEntity">The camera entity.</param>
    /// <param name="targetPosition">The target position in tile coordinates.</param>
    public void SetCameraTarget(Entity cameraEntity, Vector2 targetPosition)
    {
        if (!World.IsAlive(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.SetCameraTarget: Entity {EntityId} is not alive",
                cameraEntity.Id
            );
            return;
        }

        if (!World.Has<CameraComponent>(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.SetCameraTarget: Entity {EntityId} does not have CameraComponent",
                cameraEntity.Id
            );
            return;
        }

        ref var camera = ref World.Get<CameraComponent>(cameraEntity);
        camera.FollowTarget = targetPosition;
        camera.FollowEntity = null; // Clear entity-based following for consistency
        camera.IsDirty = true;
    }

    /// <summary>
    ///     Sets the camera to follow a target entity (updates position each frame from entity's PositionComponent).
    /// </summary>
    /// <param name="cameraEntity">The camera entity.</param>
    /// <param name="targetEntity">The target entity to follow.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if target entity lacks PositionComponent, SpriteSheetComponent, or
    ///     SpriteAnimationComponent.
    /// </exception>
    public void SetCameraFollowEntity(Entity cameraEntity, Entity targetEntity)
    {
        if (!World.IsAlive(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.SetCameraFollowEntity: Camera entity {EntityId} is not alive",
                cameraEntity.Id
            );
            return;
        }

        if (!World.Has<CameraComponent>(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.SetCameraFollowEntity: Entity {EntityId} does not have CameraComponent",
                cameraEntity.Id
            );
            return;
        }

        if (!World.IsAlive(targetEntity))
        {
            _logger.Warning(
                "CameraSystem.SetCameraFollowEntity: Target entity {EntityId} is not alive",
                targetEntity.Id
            );
            return;
        }

        // Validate target entity has PositionComponent
        if (!World.Has<PositionComponent>(targetEntity))
            throw new InvalidOperationException(
                $"CameraSystem.SetCameraFollowEntity: Target entity {targetEntity.Id} does not have PositionComponent. "
                    + "Cannot follow entity without position information."
            );

        // Validate target entity has required sprite components
        if (!World.Has<SpriteSheetComponent>(targetEntity))
            throw new InvalidOperationException(
                $"CameraSystem.SetCameraFollowEntity: Target entity {targetEntity.Id} does not have SpriteSheetComponent. "
                    + "Cannot follow entity without sprite sheet information."
            );

        if (!World.Has<SpriteAnimationComponent>(targetEntity))
            throw new InvalidOperationException(
                $"CameraSystem.SetCameraFollowEntity: Target entity {targetEntity.Id} does not have SpriteAnimationComponent. "
                    + "Cannot follow entity without animation information."
            );

        ref var camera = ref World.Get<CameraComponent>(cameraEntity);
        ref var targetPos = ref World.Get<PositionComponent>(targetEntity);

        // Calculate the center point of the entity's sprite for proper camera centering
        var entityCenter = CalculateEntityCenter(targetEntity, targetPos.Position);

        // Convert pixel position to tile coordinates using camera's tile dimensions
        var tilePos = ConvertPixelToTile(entityCenter, camera.TileWidth, camera.TileHeight);

        // Apply map bounds clamping if map bounds are set
        if (camera.MapBounds != Rectangle.Empty)
            tilePos = camera.ClampPositionToMapBounds(tilePos);

        // Immediately update camera position to match target (ensures correct position on first frame)
        camera.Position = tilePos;

        _logger.Debug(
            "CameraSystem.SetCameraFollowEntity: Updated camera position to ({X}, {Y}) tiles (player at {PlayerX}, {PlayerY} pixels)",
            tilePos.X,
            tilePos.Y,
            targetPos.Position.X,
            targetPos.Position.Y
        );

        // Set follow entity (component-based following)
        camera.FollowEntity = targetEntity;
        camera.IsFollowingLocked = false; // Allow following
        camera.FollowTarget = null; // Clear position-based following
        camera.IsDirty = true;
    }

    /// <summary>
    ///     Stops the camera from following its target.
    /// </summary>
    /// <param name="cameraEntity">The camera entity.</param>
    public void StopFollowing(Entity cameraEntity)
    {
        if (!World.Has<CameraComponent>(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.StopFollowing: Entity {EntityId} does not have CameraComponent",
                cameraEntity.Id
            );
            return;
        }

        ref var camera = ref World.Get<CameraComponent>(cameraEntity);
        camera.FollowEntity = null;
        camera.IsFollowingLocked = false;
        camera.FollowTarget = null;
        camera.IsDirty = true;
    }

    /// <summary>
    ///     Locks or unlocks camera following. When locked, the camera will not follow FollowEntity,
    ///     allowing manual camera control for cutscenes, transitions, or other scenarios.
    /// </summary>
    /// <param name="cameraEntity">The camera entity.</param>
    /// <param name="locked">True to lock following (disable), false to unlock (enable).</param>
    public void LockCameraFollowing(Entity cameraEntity, bool locked)
    {
        if (!World.Has<CameraComponent>(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.LockCameraFollowing: Entity {EntityId} does not have CameraComponent",
                cameraEntity.Id
            );
            return;
        }

        ref var camera = ref World.Get<CameraComponent>(cameraEntity);
        camera.IsFollowingLocked = locked;
        camera.IsDirty = true;
    }

    /// <summary>
    ///     Forces an immediate update of the camera position based on its follow entity.
    ///     Useful for ensuring the camera is correctly positioned after initialization or map load.
    /// </summary>
    /// <param name="cameraEntity">The camera entity.</param>
    public void UpdateCameraPosition(Entity cameraEntity)
    {
        if (!World.Has<CameraComponent>(cameraEntity))
        {
            _logger.Warning(
                "CameraSystem.UpdateCameraPosition: Entity {EntityId} does not have CameraComponent",
                cameraEntity.Id
            );
            return;
        }

        ref var camera = ref World.Get<CameraComponent>(cameraEntity);

        // Early return if no follow entity is set
        if (!camera.FollowEntity.HasValue)
        {
            _logger.Debug(
                "CameraSystem.UpdateCameraPosition: No follow entity set for camera {EntityId}, nothing to update",
                cameraEntity.Id
            );
            return;
        }

        // Force update by calling UpdateCamera with zero deltaTime
        // This will update position from follow entity if set
        UpdateCamera(ref camera, 0f);
    }
}
