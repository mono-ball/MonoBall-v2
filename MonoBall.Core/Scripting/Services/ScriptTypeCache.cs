using System;
using System.Collections.Concurrent;
using Serilog;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Thread-safe cache for compiled script types.
/// </summary>
public class ScriptTypeCache : IScriptTypeCache
{
    private readonly ConcurrentDictionary<string, Type> _compiledTypes = new();
    private readonly ILogger? _logger;

    /// <summary>
    ///     Initializes a new instance of the ScriptTypeCache class.
    /// </summary>
    /// <param name="logger">Optional logger for debugging.</param>
    public ScriptTypeCache(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public bool TryGetCompiledType(string scriptId, out Type? type)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Script ID cannot be null or empty.", nameof(scriptId));

        return _compiledTypes.TryGetValue(scriptId, out type);
    }

    /// <inheritdoc />
    public void CacheCompiledType(string scriptId, Type type)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Script ID cannot be null or empty.", nameof(scriptId));
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        _compiledTypes[scriptId] = type;
        _logger?.Debug("Cached compiled type for script: {ScriptId}", scriptId);
    }

    /// <inheritdoc />
    public int GetCompiledTypeCount()
    {
        return _compiledTypes.Count;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _compiledTypes.Clear();
        _logger?.Debug("Cleared script type cache");
    }
}
