using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores typed game variables.
///     Variables are identified by string keys and can store various types.
///     This component is pure data - all logic is handled by FlagVariableService.
/// </summary>
public struct VariablesComponent
{
    /// <summary>
    ///     Dictionary storing variable values as strings (serialized).
    ///     Values are deserialized on access based on requested type.
    ///     Must be initialized (non-null) when component is created.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; }

    /// <summary>
    ///     Type information for variables (for proper deserialization).
    ///     Key: variable key, Value: type name
    ///     Must be initialized (non-null) when component is created.
    /// </summary>
    public Dictionary<string, string> VariableTypes { get; set; }
}
