namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Composite service that provides access to all script compilation caching services.
///     Registered as singleton in Game.Services for sharing across ScriptLoaderService instances.
/// </summary>
public interface IScriptCompilationCache
{
    /// <summary>
    ///     Gets the script type cache.
    /// </summary>
    IScriptTypeCache TypeCache { get; }

    /// <summary>
    ///     Gets the dependency reference cache.
    /// </summary>
    IDependencyReferenceCache DependencyCache { get; }

    /// <summary>
    ///     Gets the script factory cache.
    /// </summary>
    IScriptFactoryCache FactoryCache { get; }

    /// <summary>
    ///     Gets the temp file manager.
    /// </summary>
    ITempFileManager TempFileManager { get; }

    /// <summary>
    ///     Clears all caches (for testing/hot-reload).
    /// </summary>
    void Clear();
}
