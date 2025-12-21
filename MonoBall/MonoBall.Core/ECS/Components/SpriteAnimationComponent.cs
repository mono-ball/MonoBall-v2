namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores the current animation state for a sprite.
    /// </summary>
    public struct SpriteAnimationComponent
    {
        /// <summary>
        /// The name of the current animation.
        /// </summary>
        public string CurrentAnimationName { get; set; }

        /// <summary>
        /// The current frame index in the animation sequence (0-based).
        /// </summary>
        public int CurrentFrameIndex { get; set; }

        /// <summary>
        /// Time elapsed on the current frame in seconds.
        /// </summary>
        public float ElapsedTime { get; set; }

        /// <summary>
        /// Whether to flip the sprite horizontally for the current animation.
        /// </summary>
        public bool FlipHorizontal { get; set; }
    }
}
