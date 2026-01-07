using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that stores typed variables for a specific entity.
///     Similar to VariablesComponent but scoped to individual entities.
///     This component is pure data - all logic is handled by FlagVariableService.
/// </summary>
public struct EntityVariablesComponent
{
    /// <summary>
    ///     Dictionary storing variable values as strings (serialized).
    /// </summary>
    public Dictionary<string, string> Variables { get; set; }

    /// <summary>
    ///     Type information for variables (for proper deserialization).
    /// </summary>
    public Dictionary<string, string> VariableTypes { get; set; }
}
