namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores NPC identity and configuration data.
    /// </summary>
    public struct NpcComponent
    {
        /// <summary>
        /// The unique NPC definition ID.
        /// </summary>
        public string NpcId { get; set; }

        /// <summary>
        /// The name of the NPC.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The sprite ID for this NPC.
        /// </summary>
        public string SpriteId { get; set; }

        /// <summary>
        /// The map ID that contains this NPC.
        /// </summary>
        public string MapId { get; set; }

        /// <summary>
        /// The elevation (z-order) of the NPC.
        /// </summary>
        public int Elevation { get; set; }

        /// <summary>
        /// The visibility flag for conditional visibility (null if always visible).
        /// </summary>
        public string? VisibilityFlag { get; set; }

        /// <summary>
        /// The behavior definition ID for this NPC (e.g., "base:behavior:movement/wander").
        /// References a BehaviorDefinition that contains the behavior script.
        /// </summary>
        public string? BehaviorId { get; set; }
    }
}
