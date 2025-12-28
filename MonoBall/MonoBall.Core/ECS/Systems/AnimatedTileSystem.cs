using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Maps;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for updating animation timers and advancing frames for animated tiles.
    /// </summary>
    public class AnimatedTileSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly IResourceManager _resourceManager;
        private readonly QueryDescription _queryDescription;

        // Reusable collection to avoid allocations in hot paths
        private readonly List<int> _tileIndexList = new List<int>();

        private readonly ILogger _logger;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.AnimatedTile;

        /// <summary>
        /// Initializes a new instance of the AnimatedTileSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="resourceManager">The resource manager for accessing tile animation frame cache.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public AnimatedTileSystem(World world, IResourceManager resourceManager, ILogger logger)
            : base(world)
        {
            _resourceManager =
                resourceManager ?? throw new System.ArgumentNullException(nameof(resourceManager));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            _queryDescription = new QueryDescription().WithAll<
                AnimatedTileDataComponent,
                TileDataComponent
            >();
        }

        /// <summary>
        /// Updates animation timers and advances frames for all animated tiles.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        public override void Update(in float deltaTime)
        {
            float dt = deltaTime; // Copy to avoid ref parameter in lambda
            World.Query(
                in _queryDescription,
                (ref AnimatedTileDataComponent animData, ref TileDataComponent tileData) =>
                {
                    UpdateAnimations(ref animData, ref tileData, dt);
                }
            );
        }

        /// <summary>
        /// Updates animation timers and advances frames for a single chunk's animated tiles.
        /// </summary>
        /// <param name="animData">The animated tile data component.</param>
        /// <param name="tileData">The tile data component.</param>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        private void UpdateAnimations(
            ref AnimatedTileDataComponent animData,
            ref TileDataComponent tileData,
            float deltaTime
        )
        {
            if (animData.AnimatedTiles == null || animData.AnimatedTiles.Count == 0)
            {
                return;
            }

            // Create list of keys to iterate over (to avoid modifying dictionary during enumeration)
            // Reuse the collection to avoid allocation
            _tileIndexList.Clear();
            _tileIndexList.AddRange(animData.AnimatedTiles.Keys);

            foreach (var tileIndex in _tileIndexList)
            {
                var animState = animData.AnimatedTiles[tileIndex];

                // Get animation frames from cache
                var frames = _resourceManager.GetCachedTileAnimation(
                    animState.AnimationTilesetId,
                    animState.AnimationLocalTileId
                );

                if (frames == null || frames.Count == 0)
                {
                    _logger.Warning(
                        "AnimatedTileSystem.UpdateAnimations: Animation frames not found in cache for tileset {TilesetId}, localTileId {LocalTileId}",
                        animState.AnimationTilesetId,
                        animState.AnimationLocalTileId
                    );
                    continue;
                }

                // Update elapsed time
                animState.ElapsedTime += deltaTime;

                // Get current frame duration in seconds
                float frameDurationSeconds =
                    frames[animState.CurrentFrameIndex].DurationMs / 1000.0f;

                // Advance to next frame if duration exceeded
                while (animState.ElapsedTime >= frameDurationSeconds)
                {
                    // Subtract frame duration for frame-perfect timing
                    animState.ElapsedTime -= frameDurationSeconds;

                    // Advance to next frame (loop to 0 when reaching end)
                    animState.CurrentFrameIndex++;
                    if (animState.CurrentFrameIndex >= frames.Count)
                    {
                        animState.CurrentFrameIndex = 0;
                    }

                    // Get new frame duration
                    frameDurationSeconds = frames[animState.CurrentFrameIndex].DurationMs / 1000.0f;
                }

                // Update the animation state in the dictionary
                animData.AnimatedTiles[tileIndex] = animState;
            }
        }
    }
}
