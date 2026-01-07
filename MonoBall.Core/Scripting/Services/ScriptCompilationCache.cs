using System;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Composite service that provides access to all script compilation caching services.
///     Registered as singleton in Game.Services.
/// </summary>
public class ScriptCompilationCache : IScriptCompilationCache
{
    /// <summary>
    ///     Initializes a new instance of the ScriptCompilationCache class.
    /// </summary>
    /// <param name="typeCache">The script type cache.</param>
    /// <param name="dependencyCache">The dependency reference cache.</param>
    /// <param name="factoryCache">The script factory cache.</param>
    /// <param name="tempFileManager">The temp file manager.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ScriptCompilationCache(
        IScriptTypeCache typeCache,
        IDependencyReferenceCache dependencyCache,
        IScriptFactoryCache factoryCache,
        ITempFileManager tempFileManager
    )
    {
        TypeCache = typeCache ?? throw new ArgumentNullException(nameof(typeCache));
        DependencyCache =
            dependencyCache ?? throw new ArgumentNullException(nameof(dependencyCache));
        FactoryCache = factoryCache ?? throw new ArgumentNullException(nameof(factoryCache));
        TempFileManager =
            tempFileManager ?? throw new ArgumentNullException(nameof(tempFileManager));
    }

    /// <inheritdoc />
    public IScriptTypeCache TypeCache { get; }

    /// <inheritdoc />
    public IDependencyReferenceCache DependencyCache { get; }

    /// <inheritdoc />
    public IScriptFactoryCache FactoryCache { get; }

    /// <inheritdoc />
    public ITempFileManager TempFileManager { get; }

    /// <inheritdoc />
    public void Clear()
    {
        TypeCache.Clear();
        DependencyCache.Clear();
        FactoryCache.Clear();
    }
}
