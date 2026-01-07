using Arch.Core;

namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores popup display data for map popups.
/// </summary>
public struct MapPopupComponent
{
    /// <summary>
    ///     Gets or sets the map section name to display (e.g., "LITTLEROOT TOWN").
    /// </summary>
    public string MapSectionName { get; set; }

    /// <summary>
    ///     Gets or sets the popup theme ID.
    /// </summary>
    public string ThemeId { get; set; }

    /// <summary>
    ///     Gets or sets the background definition ID (resolved from theme).
    /// </summary>
    public string BackgroundId { get; set; }

    /// <summary>
    ///     Gets or sets the outline definition ID (resolved from theme).
    /// </summary>
    public string OutlineId { get; set; }

    /// <summary>
    ///     Gets or sets the scene entity associated with this popup.
    /// </summary>
    public Entity SceneEntity { get; set; }
}
