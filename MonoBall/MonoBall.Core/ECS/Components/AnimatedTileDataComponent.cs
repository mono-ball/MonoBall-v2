using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores animation state for animated tiles within a chunk.
    /// Only attached to chunks that contain animated tiles.
    /// </summary>
    public struct AnimatedTileDataComponent
    {
        /// <summary>
        /// Dictionary mapping tile indices (position in chunk's TileIndices array) to their animation state.
        /// Only contains entries for tiles that are animated.
        /// </summary>
        public Dictionary<int, TileAnimationState> AnimatedTiles { get; set; }
    }

    /// <summary>
    /// Stores the animation state for a single animated tile.
    /// Animation frames are not stored here - they're looked up from cache using the animation key.
    /// </summary>
    public struct TileAnimationState
    {
        /// <summary>
        /// The tileset ID for cache lookup of animation frames.
        /// </summary>
        public string AnimationTilesetId { get; set; }

        /// <summary>
        /// The local tile ID for cache lookup of animation frames.
        /// </summary>
        public int AnimationLocalTileId { get; set; }

        /// <summary>
        /// Current frame index in the animation sequence (0-based).
        /// </summary>
        public int CurrentFrameIndex { get; set; }

        /// <summary>
        /// Time elapsed on the current frame in seconds.
        /// </summary>
        public float ElapsedTime { get; set; }
    }
}
