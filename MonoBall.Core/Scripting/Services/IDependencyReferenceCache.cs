using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using MonoBall.Core.Mods;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Caches dependency references per mod to avoid repeated resolution.
/// </summary>
public interface IDependencyReferenceCache
{
    /// <summary>
    ///     Gets or resolves dependency references for a mod.
    /// </summary>
    /// <param name="mod">The mod manifest to resolve dependencies for.</param>
    /// <param name="resolver">The resolver function to use if dependencies are not cached.</param>
    /// <returns>List of metadata references for the mod's dependencies.</returns>
    /// <exception cref="ArgumentNullException">Thrown when mod or resolver is null.</exception>
    List<MetadataReference> GetOrResolveDependencies(
        ModManifest mod,
        Func<ModManifest, List<MetadataReference>> resolver
    );

    /// <summary>
    ///     Clears all cached dependency references.
    /// </summary>
    void Clear();
}
