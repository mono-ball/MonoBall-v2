using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Thread-safe cache for dependency references per mod.
/// </summary>
public class DependencyReferenceCache : IDependencyReferenceCache
{
    private readonly ConcurrentDictionary<string, List<MetadataReference>> _dependencyCache = new();
    private readonly ILogger? _logger;

    /// <summary>
    ///     Initializes a new instance of the DependencyReferenceCache class.
    /// </summary>
    /// <param name="logger">Optional logger for debugging.</param>
    public DependencyReferenceCache(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public List<MetadataReference> GetOrResolveDependencies(
        ModManifest mod,
        Func<ModManifest, List<MetadataReference>> resolver
    )
    {
        if (mod == null)
            throw new ArgumentNullException(nameof(mod));
        if (resolver == null)
            throw new ArgumentNullException(nameof(resolver));

        return _dependencyCache.GetOrAdd(
            mod.Id,
            _ =>
            {
                _logger?.Debug("Resolving dependencies for mod: {ModId}", mod.Id);
                var references = resolver(mod);
                _logger?.Debug(
                    "Resolved {Count} dependency references for mod: {ModId}",
                    references.Count,
                    mod.Id
                );
                return references;
            }
        );
    }

    /// <inheritdoc />
    public void Clear()
    {
        _dependencyCache.Clear();
        _logger?.Debug("Cleared dependency reference cache");
    }
}
