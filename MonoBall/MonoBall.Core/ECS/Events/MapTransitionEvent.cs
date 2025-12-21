namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when transitioning between maps.
    /// </summary>
    public struct MapTransitionEvent
    {
        /// <summary>
        /// The source map ID.
        /// </summary>
        public string SourceMapId { get; set; }

        /// <summary>
        /// The target map ID.
        /// </summary>
        public string TargetMapId { get; set; }

        /// <summary>
        /// The transition direction.
        /// </summary>
        public Components.MapConnectionDirection Direction { get; set; }

        /// <summary>
        /// The offset in tiles for the transition.
        /// </summary>
        public int Offset { get; set; }
    }
}
