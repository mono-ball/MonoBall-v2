using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for updating camera positions, following targets, and enforcing bounds.
    /// </summary>
    public class CameraSystem : BaseSystem<World, float>
    {
        private readonly QueryDescription _queryDescription;

        /// <summary>
        /// Initializes a new instance of the CameraSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        public CameraSystem(World world)
            : base(world)
        {
            _queryDescription = new QueryDescription().WithAll<CameraComponent>();
        }

        /// <summary>
        /// Updates camera positions, following logic, and bounds enforcement.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime; // Copy to avoid ref parameter in lambda
            World.Query(
                in _queryDescription,
                (ref CameraComponent camera) =>
                {
                    UpdateCamera(ref camera, dt);
                }
            );
        }

        /// <summary>
        /// Updates a single camera.
        /// </summary>
        private void UpdateCamera(ref CameraComponent camera, float deltaTime)
        {
            if (!camera.IsActive)
            {
                return;
            }

            // Handle target following (FollowTarget is a Vector2? position to follow)
            if (camera.FollowTarget.HasValue)
            {
                Vector2 targetPos = camera.FollowTarget.Value;

                // Apply smoothing if enabled
                if (camera.SmoothingSpeed > 0 && camera.SmoothingSpeed < 1)
                {
                    camera.Position = Vector2.Lerp(
                        camera.Position,
                        targetPos,
                        camera.SmoothingSpeed
                    );
                }
                else
                {
                    camera.Position = targetPos;
                }

                camera.IsDirty = true;
            }

            // Enforce map bounds
            if (camera.MapBounds != Rectangle.Empty)
            {
                camera.Position = camera.ClampPositionToMapBounds(camera.Position);
            }
        }

        /// <summary>
        /// Sets the camera position.
        /// </summary>
        /// <param name="cameraEntity">The camera entity.</param>
        /// <param name="position">The new position.</param>
        public void SetCameraPosition(Entity cameraEntity, Vector2 position)
        {
            if (!World.Has<CameraComponent>(cameraEntity))
            {
                Log.Warning(
                    "CameraSystem.SetCameraPosition: Entity {EntityId} does not have CameraComponent",
                    cameraEntity.Id
                );
                return;
            }

            ref var camera = ref World.Get<CameraComponent>(cameraEntity);
            camera.Position = position;
        }

        /// <summary>
        /// Sets the camera to follow a target position (in tile coordinates).
        /// </summary>
        /// <param name="cameraEntity">The camera entity.</param>
        /// <param name="targetPosition">The target position in tile coordinates.</param>
        public void SetCameraTarget(Entity cameraEntity, Vector2 targetPosition)
        {
            if (!World.Has<CameraComponent>(cameraEntity))
            {
                Log.Warning(
                    "CameraSystem.SetCameraTarget: Entity {EntityId} does not have CameraComponent",
                    cameraEntity.Id
                );
                return;
            }

            ref var camera = ref World.Get<CameraComponent>(cameraEntity);
            camera.FollowTarget = targetPosition;
            camera.IsDirty = true;
        }

        /// <summary>
        /// Sets the camera to follow a target entity (updates FollowTarget each frame).
        /// </summary>
        /// <param name="cameraEntity">The camera entity.</param>
        /// <param name="targetEntity">The target entity to follow.</param>
        public void SetCameraFollowEntity(Entity cameraEntity, Entity targetEntity)
        {
            if (!World.Has<CameraComponent>(cameraEntity))
            {
                Log.Warning(
                    "CameraSystem.SetCameraFollowEntity: Entity {EntityId} does not have CameraComponent",
                    cameraEntity.Id
                );
                return;
            }

            // Get target position from entity
            if (!World.Has<PositionComponent>(targetEntity))
            {
                Log.Warning(
                    "CameraSystem.SetCameraFollowEntity: Target entity {EntityId} does not have PositionComponent",
                    targetEntity.Id
                );
                return;
            }

            ref var targetPos = ref World.Get<PositionComponent>(targetEntity);
            ref var camera = ref World.Get<CameraComponent>(cameraEntity);

            // Convert pixel position to tile position using camera's tile dimensions
            Vector2 tilePos = new Vector2(
                targetPos.Position.X / camera.TileWidth,
                targetPos.Position.Y / camera.TileHeight
            );

            camera.FollowTarget = tilePos;
            camera.IsDirty = true;
        }

        /// <summary>
        /// Stops the camera from following its target.
        /// </summary>
        /// <param name="cameraEntity">The camera entity.</param>
        public void StopFollowing(Entity cameraEntity)
        {
            if (!World.Has<CameraComponent>(cameraEntity))
            {
                return;
            }

            ref var camera = ref World.Get<CameraComponent>(cameraEntity);
            camera.FollowTarget = null;
            camera.IsDirty = true;
        }
    }
}
