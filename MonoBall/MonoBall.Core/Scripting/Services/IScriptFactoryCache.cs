using System;
using MonoBall.Core.Scripting.Runtime;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Compiles and caches delegate factories for fast script instantiation.
/// </summary>
public interface IScriptFactoryCache
{
    /// <summary>
    ///     Gets or creates a compiled delegate factory for a script type.
    /// </summary>
    /// <param name="scriptType">The script type to create a factory for.</param>
    /// <returns>A compiled delegate factory that creates instances of the script type, or null if factory creation failed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when scriptType is null.</exception>
    Func<ScriptBase>? GetOrCreateFactory(Type scriptType);

    /// <summary>
    ///     Clears all cached factories.
    /// </summary>
    void Clear();
}
