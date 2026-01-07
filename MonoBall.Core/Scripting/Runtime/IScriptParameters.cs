using System.Collections.Generic;

namespace MonoBall.Core.Scripting.Runtime;

/// <summary>
///     Interface for accessing script parameters.
///     Provides type-safe access to script configuration parameters.
/// </summary>
public interface IScriptParameters
{
    /// <summary>
    ///     Gets a script parameter value by name.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <param name="defaultValue">Optional default value if parameter not found.</param>
    /// <returns>The parameter value, or defaultValue if not found.</returns>
    T GetParameter<T>(string name, T? defaultValue = default);

    /// <summary>
    ///     Gets all script parameters as a read-only dictionary.
    /// </summary>
    /// <returns>Dictionary of parameter names to values.</returns>
    IReadOnlyDictionary<string, object> GetParameters();
}
