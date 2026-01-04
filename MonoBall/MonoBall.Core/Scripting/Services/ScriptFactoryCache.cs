using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using MonoBall.Core.Scripting.Runtime;
using Serilog;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Thread-safe cache for compiled delegate factories.
/// </summary>
public class ScriptFactoryCache : IScriptFactoryCache
{
    private readonly ConcurrentDictionary<Type, Func<ScriptBase>?> _factoryCache = new();
    private readonly ILogger? _logger;

    /// <summary>
    ///     Initializes a new instance of the ScriptFactoryCache class.
    /// </summary>
    /// <param name="logger">Optional logger for debugging.</param>
    public ScriptFactoryCache(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Func<ScriptBase>? GetOrCreateFactory(Type scriptType)
    {
        if (scriptType == null)
            throw new ArgumentNullException(nameof(scriptType));

        return _factoryCache.GetOrAdd(scriptType, CreateFactory);
    }

    /// <inheritdoc />
    public void Clear()
    {
        _factoryCache.Clear();
        _logger?.Debug("Cleared script factory cache");
    }

    private Func<ScriptBase>? CreateFactory(Type scriptType)
    {
        try
        {
            var constructor = scriptType.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
            {
                _logger?.Error(
                    "Script type {ScriptType} does not have a parameterless constructor.",
                    scriptType.Name
                );
                return null;
            }

            // Compile delegate: () => new ScriptType()
            var newExpr = Expression.New(constructor);
            var lambda = Expression.Lambda<Func<ScriptBase>>(newExpr);
            var factory = lambda.Compile();

            _logger?.Debug("Created factory for script type: {ScriptType}", scriptType.Name);
            return factory;
        }
        catch (Exception ex)
        {
            _logger?.Error(
                ex,
                "Failed to create factory for script type {ScriptType}",
                scriptType.Name
            );
            return null;
        }
    }
}
