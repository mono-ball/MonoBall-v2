namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a map is unloaded.
/// </summary>
public struct MapUnloadedEvent
{
    /// <summary>
    ///     The map definition ID that was unloaded.
    /// </summary>
    public string MapId { get; set; }
}
