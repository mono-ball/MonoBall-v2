using System;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Caches compiled script types for reuse across ScriptLoaderService instances.
/// </summary>
public interface IScriptTypeCache
{
    /// <summary>
    ///     Gets a compiled script type from cache, or null if not found.
    /// </summary>
    /// <param name="scriptId">The script definition ID.</param>
    /// <param name="type">When this method returns, contains the compiled type if found; otherwise, null.</param>
    /// <returns>True if the type was found in cache, false otherwise.</returns>
    /// <exception cref="ArgumentException">Thrown when scriptId is null or empty.</exception>
    bool TryGetCompiledType(string scriptId, out Type? type);

    /// <summary>
    ///     Caches a compiled script type.
    /// </summary>
    /// <param name="scriptId">The script definition ID.</param>
    /// <param name="type">The compiled script type.</param>
    /// <exception cref="ArgumentException">Thrown when scriptId is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when type is null.</exception>
    void CacheCompiledType(string scriptId, Type type);

    /// <summary>
    ///     Gets the number of compiled script types in the cache.
    /// </summary>
    /// <returns>The number of cached script types.</returns>
    int GetCompiledTypeCount();

    /// <summary>
    ///     Clears all cached script types.
    /// </summary>
    void Clear();
}
