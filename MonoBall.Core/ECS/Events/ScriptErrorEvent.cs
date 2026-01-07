using System;
using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a script encounters an error.
/// </summary>
public struct ScriptErrorEvent
{
    /// <summary>
    ///     The entity the script is attached to (null for plugin scripts).
    /// </summary>
    public Entity? Entity { get; set; }

    /// <summary>
    ///     The script definition ID.
    /// </summary>
    public string ScriptDefinitionId { get; set; }

    /// <summary>
    ///     The exception that occurred.
    /// </summary>
    public Exception Exception { get; set; }

    /// <summary>
    ///     The error message.
    /// </summary>
    public string ErrorMessage { get; set; }

    /// <summary>
    ///     Timestamp when the error occurred.
    /// </summary>
    public DateTime ErrorAt { get; set; }
}
