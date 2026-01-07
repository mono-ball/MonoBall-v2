using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when an NPC is loaded.
/// </summary>
public struct NpcLoadedEvent
{
    /// <summary>
    ///     The entity reference for the NPC.
    /// </summary>
    public Entity NpcEntity { get; set; }

    /// <summary>
    ///     The NPC definition ID.
    /// </summary>
    public string NpcId { get; set; }

    /// <summary>
    ///     The map ID that contains this NPC.
    /// </summary>
    public string MapId { get; set; }
}
