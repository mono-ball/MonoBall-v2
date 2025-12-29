using System;
using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a script is loaded and initialized.
/// </summary>
public struct ScriptLoadedEvent
{
    /// <summary>
    ///     The entity the script is attached to.
    /// </summary>
    public Entity Entity { get; set; }

    /// <summary>
    ///     The script definition ID.
    /// </summary>
    public string ScriptDefinitionId { get; set; }

    /// <summary>
    ///     Timestamp when the script was loaded.
    /// </summary>
    public DateTime LoadedAt { get; set; }
}
