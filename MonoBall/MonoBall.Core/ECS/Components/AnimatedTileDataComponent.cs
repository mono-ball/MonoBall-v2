using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores animation state for animated tiles within a chunk.
    /// Only attached to chunks that contain animated tiles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Architecture Note:</b> This component contains a <see cref="Dictionary{TKey, TValue}"/>
    /// (reference type) which violates strict ECS principles that components should be pure value types.
    /// This design decision is made for practical reasons:
    /// </para>
    /// <list type="bullet">
    /// <item>Only a subset of tiles in a chunk are animated (sparse data)</item>
    /// <item>Dictionary provides efficient O(1) lookups by tile index</item>
    /// <item>Alternative designs (fixed-size arrays) would waste memory for non-animated tiles</item>
    /// </list>
    /// <para>
    /// <b>Important:</b> This component MUST be initialized with a non-null Dictionary.
    /// MapLoaderSystem.CreateTileChunks() ensures this by always initializing AnimatedTiles when
    /// creating chunks with animated tiles. Systems using this component should always check for
    /// null before accessing AnimatedTiles.
    /// </para>
    /// </remarks>
    public struct AnimatedTileDataComponent
    {
        /// <summary>
        /// Dictionary mapping tile indices (position in chunk's TileIndices array) to their animation state.
        /// Only contains entries for tiles that are animated.
        /// Must be initialized (non-null) when the component is created.
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
