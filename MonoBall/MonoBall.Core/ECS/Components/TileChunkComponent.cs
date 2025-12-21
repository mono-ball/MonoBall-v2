namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that represents a chunk of tiles for batch rendering.
    /// </summary>
    public struct TileChunkComponent
    {
        /// <summary>
        /// The chunk X position in tile coordinates.
        /// </summary>
        public int ChunkX { get; set; }

        /// <summary>
        /// The chunk Y position in tile coordinates.
        /// </summary>
        public int ChunkY { get; set; }

        /// <summary>
        /// The width of the chunk in tiles.
        /// </summary>
        public int ChunkWidth { get; set; }

        /// <summary>
        /// The height of the chunk in tiles.
        /// </summary>
        public int ChunkHeight { get; set; }

        /// <summary>
        /// The layer ID this chunk belongs to.
        /// </summary>
        public string LayerId { get; set; }

        /// <summary>
        /// The layer index for rendering order.
        /// </summary>
        public int LayerIndex { get; set; }
    }
}
