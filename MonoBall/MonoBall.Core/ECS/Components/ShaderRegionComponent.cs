namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component for map shader regions - areas that apply shaders when player enters.
    /// Saved shader states are stored externally in ShaderRegionDetectionSystem
    /// to avoid Dictionary allocations in ECS components (per Arch ECS best practices).
    /// </summary>
    public struct ShaderRegionComponent
    {
        /// <summary>
        /// The map ID this region belongs to.
        /// </summary>
        public string MapId { get; set; }

        /// <summary>
        /// Unique identifier for this region within the map.
        /// </summary>
        public string RegionId { get; set; }

        /// <summary>
        /// X coordinate of region's top-left corner (in tiles).
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Y coordinate of region's top-left corner (in tiles).
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Width of the region (in tiles).
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Height of the region (in tiles).
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// The shader ID to apply when entering this region (null for none).
        /// </summary>
        public string? LayerShaderId { get; set; }

        /// <summary>
        /// The layer to apply the shader to.
        /// </summary>
        public ShaderLayer TargetLayer { get; set; }

        /// <summary>
        /// Duration of shader transition when entering/exiting.
        /// </summary>
        public float TransitionDuration { get; set; }

        /// <summary>
        /// Easing function for transitions.
        /// </summary>
        public EasingFunction TransitionEasing { get; set; }

        /// <summary>
        /// Priority for overlapping regions (higher priority wins).
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// Whether the player is currently inside this region.
        /// Tracked by ShaderRegionDetectionSystem.
        /// </summary>
        public bool IsPlayerInside { get; set; }

        /// <summary>
        /// Checks if a tile coordinate is inside this region.
        /// </summary>
        public readonly bool Contains(int tileX, int tileY)
        {
            return tileX >= X && tileX < X + Width && tileY >= Y && tileY < Y + Height;
        }
    }
}
