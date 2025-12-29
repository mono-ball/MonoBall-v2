using Arch.Core;

namespace MonoBall.Core.ECS.Events;

/// <summary>
///     Event fired when a variable value changes.
/// </summary>
public struct VariableChangedEvent
{
    /// <summary>
    ///     The variable key that changed.
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    ///     The entity this variable belongs to. Null if this is a global variable.
    /// </summary>
    public Entity? Entity { get; set; }

    /// <summary>
    ///     The previous value as a serialized string representation.
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    ///     The new value as a serialized string representation.
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    ///     The type name of the old value (for deserialization).
    /// </summary>
    public string? OldType { get; set; }

    /// <summary>
    ///     The type name of the new value (for deserialization).
    /// </summary>
    public string? NewType { get; set; }
}
