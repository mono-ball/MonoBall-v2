namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired to show a map popup.
    /// </summary>
    public struct MapPopupShowEvent
    {
        /// <summary>
        /// Gets or sets the map section ID.
        /// </summary>
        public string MapSectionId { get; set; }

        /// <summary>
        /// Gets or sets the map section display name.
        /// </summary>
        public string MapSectionName { get; set; }

        /// <summary>
        /// Gets or sets the popup theme ID.
        /// </summary>
        public string ThemeId { get; set; }
    }
}
