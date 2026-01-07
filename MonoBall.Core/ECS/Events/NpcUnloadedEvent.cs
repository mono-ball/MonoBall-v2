namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when an NPC is unloaded.
/// </summary>
public struct NpcUnloadedEvent
{
    /// <summary>
    ///     The NPC definition ID.
    /// </summary>
    public string NpcId { get; set; }

    /// <summary>
    ///     The map ID that contained this NPC.
    /// </summary>
    public string MapId { get; set; }
}
