using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a map is loaded.
/// </summary>
public struct MapLoadedEvent
{
    /// <summary>
    ///     The map definition ID that was loaded.
    /// </summary>
    public string MapId { get; set; }

    /// <summary>
    ///     The entity reference for the map.
    /// </summary>
    public Entity MapEntity { get; set; }
}
