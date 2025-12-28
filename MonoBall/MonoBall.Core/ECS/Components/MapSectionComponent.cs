namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component attached to map entities to specify map section and popup theme.
    /// Allows runtime modification of popup behavior.
    /// </summary>
    public struct MapSectionComponent
    {
        /// <summary>
        /// The map section definition ID.
        /// </summary>
        public string MapSectionId { get; set; }

        /// <summary>
        /// The popup theme ID for this map section.
        /// </summary>
        public string PopupThemeId { get; set; }
    }
}
