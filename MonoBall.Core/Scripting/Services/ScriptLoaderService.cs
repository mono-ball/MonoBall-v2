using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arch.Core;
using Microsoft.CodeAnalysis;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Resources;
using MonoBall.Core.Scripting.Runtime;
using Serilog;

namespace MonoBall.Core.Scripting.Services;

/// <summary>
///     Service for loading and compiling scripts. Handles file I/O and compilation.
///     Not an ECS system - operates on file system and mod registry.
/// </summary>
public class ScriptLoaderService : IDisposable
{
    private readonly IScriptCompilationCache _compilationCache;
    private readonly ScriptCompilerService _compiler;
    private readonly ILogger _logger;
    private readonly ModManager _modManager;
    private readonly ConcurrentDictionary<string, List<ScriptBase>> _pluginScriptsByMod = new();
    private readonly ConcurrentDictionary<string, Type> _pluginScriptTypes = new();
    private readonly DefinitionRegistry _registry;
    private readonly IResourceManager _resourceManager;

    /// <summary>
    ///     Initializes a new instance of the ScriptLoaderService class.
    /// </summary>
    /// <param name="compiler">The script compiler service.</param>
    /// <param name="registry">The definition registry.</param>
    /// <param name="modManager">The mod manager.</param>
    /// <param name="resourceManager">The resource manager for loading script files.</param>
    /// <param name="compilationCache">The shared script compilation cache.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ScriptLoaderService(
        ScriptCompilerService compiler,
        DefinitionRegistry registry,
        ModManager modManager,
        IResourceManager resourceManager,
        IScriptCompilationCache compilationCache,
        ILogger logger
    )
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _resourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _compilationCache =
            compilationCache ?? throw new ArgumentNullException(nameof(compilationCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Disposes of all resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Pre-loads all scripts during mod loading phase.
    ///     Compiles and caches script types (not instances).
    ///     Plugin scripts are compiled but NOT initialized here.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when script compilation fails, registry/mod manager is in invalid state,
    ///     or script definition metadata is missing.
    /// </exception>
    public void PreloadAllScripts()
    {
        _logger.Information("Pre-loading all scripts");

        var scriptDefinitionIds = _registry.GetByType("Script").ToList();
        var totalScripts = scriptDefinitionIds.Count;

        if (totalScripts == 0)
        {
            _logger.Information("No scripts to preload");
            // Still load plugin scripts
            LoadPluginScripts();
            return;
        }

        // Quick optimization: check if all scripts are already cached
        // Note: This is a best-effort check - parallel compilation handles race conditions correctly
        try
        {
            var allCached = true;
            foreach (var scriptDefId in scriptDefinitionIds)
            {
                if (!_compilationCache.TypeCache.TryGetCompiledType(scriptDefId, out _))
                {
                    allCached = false;
                    break; // Early exit - found one that's not cached
                }
            }

            if (allCached)
            {
                _logger.Information(
                    "All {Count} scripts already cached, skipping preload",
                    totalScripts
                );
                // Still load plugin scripts
                LoadPluginScripts();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error checking script cache, proceeding with full preload");
            // Continue with full preload - don't fail fast for cache check errors
        }

        // Group scripts by mod to share dependency resolution
        var scriptsByMod = scriptDefinitionIds
            .GroupBy(id =>
            {
                var metadata = _registry.GetById(id);
                if (metadata == null)
                {
                    _logger.Warning(
                        "Script definition metadata not found for {ScriptId}, using 'unknown' mod",
                        id
                    );
                    return "unknown";
                }
                return metadata.OriginalModId;
            })
            .ToList();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8), // Cap at 8 threads
        };

        var processedCount = 0;
        var cachedCount = 0;

        Parallel.ForEach(
            scriptsByMod,
            parallelOptions,
            modGroup =>
            {
                var modId = modGroup.Key;
                var modManifest = _modManager.GetModManifest(modId);

                if (modManifest == null)
                {
                    _logger.Warning("Mod manifest not found for mod {ModId}", modId);
                    return;
                }

                // Resolve dependencies once per mod (cached)
                var dependencyReferences =
                    _compilationCache.DependencyCache.GetOrResolveDependencies(
                        modManifest,
                        ResolveDependencyAssemblies
                    );

                foreach (var scriptDefId in modGroup)
                {
                    // Check cache first (handles race conditions - each script checks individually)
                    if (_compilationCache.TypeCache.TryGetCompiledType(scriptDefId, out _))
                    {
                        Interlocked.Increment(ref cachedCount);
                        Interlocked.Increment(ref processedCount);
                        continue; // Already compiled
                    }

                    try
                    {
                        var scriptDef = _registry.GetById<ScriptDefinition>(scriptDefId);
                        if (scriptDef == null)
                        {
                            _logger.Warning("Script definition not found: {ScriptId}", scriptDefId);
                            continue;
                        }

                        LoadScriptFromDefinition(scriptDef, dependencyReferences);
                        Interlocked.Increment(ref processedCount);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            ex,
                            "Failed to preload script definition {ScriptId}",
                            scriptDefId
                        );
                        // Continue with other scripts - don't stop parallel compilation
                    }
                }
            }
        );

        // Load plugin scripts (not parallelized, as they're typically fewer)
        LoadPluginScripts();

        _logger.Information(
            "Pre-loaded {Count} scripts ({Cached} from cache, {Compiled} compiled) and {PluginCount} plugin scripts",
            processedCount,
            cachedCount,
            processedCount - cachedCount,
            _pluginScriptTypes.Count
        );
    }

    /// <summary>
    ///     Loads plugin scripts from mod manifests.
    /// </summary>
    private void LoadPluginScripts()
    {
        // Load plugin scripts from mod manifests
        foreach (var mod in _modManager.LoadedMods)
            if (mod.Plugins != null && mod.Plugins.Count > 0)
                foreach (var scriptPath in mod.Plugins)
                    try
                    {
                        LoadPluginScript(mod.Id, scriptPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(
                            ex,
                            "Failed to preload plugin script {ScriptPath} for mod {ModId}. Continuing with other scripts.",
                            scriptPath,
                            mod.Id
                        );
                        // Continue loading other scripts even if one fails
                    }
    }

    /// <summary>
    ///     Creates a new script instance from a cached compiled type.
    ///     Each entity gets its own instance.
    /// </summary>
    /// <param name="definitionId">The script definition ID.</param>
    /// <returns>A new script instance.</returns>
    /// <exception cref="ArgumentException">Thrown when definition ID is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when script type is not found in cache or instance creation fails.</exception>
    public ScriptBase CreateScriptInstance(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
            throw new ArgumentException(
                "Definition ID cannot be null or empty",
                nameof(definitionId)
            );

        _logger.Debug("CreateScriptInstance called for {DefinitionId}", definitionId);

        // Get from shared cache
        if (!_compilationCache.TypeCache.TryGetCompiledType(definitionId, out var scriptType))
        {
            throw new InvalidOperationException(
                $"Script type not found in cache for definition '{definitionId}'. "
                    + "Ensure the script was pre-loaded during mod loading phase."
            );
        }

        // Use compiled delegate factory (much faster than Activator.CreateInstance)
        // scriptType is guaranteed non-null here because we throw if TryGetCompiledType returns false
        var factory = _compilationCache.FactoryCache.GetOrCreateFactory(scriptType!);
        if (factory == null)
        {
            throw new InvalidOperationException(
                $"Failed to create factory for script type '{scriptType.Name}'. "
                    + "The script type may not have a parameterless constructor."
            );
        }

        try
        {
            var instance = factory();
            if (instance == null)
            {
                var errorMessage =
                    $"Failed to create script instance for '{definitionId}': "
                    + "Factory returned null. "
                    + "This indicates a compilation issue - script type should inherit from ScriptBase.";
                _logger.Error(
                    "Failed to create script instance for {DefinitionId}: Factory returned null",
                    definitionId
                );
                throw new InvalidOperationException(errorMessage);
            }

            return instance;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create script instance for '{definitionId}': {ex.Message}",
                ex
            );
        }
    }

    /// <summary>
    ///     Initializes all plugin scripts with the provided API provider.
    ///     Called after API provider is ready.
    /// </summary>
    /// <param name="apiProvider">The script API provider.</param>
    /// <param name="world">The ECS world.</param>
    /// <param name="logger">The logger instance.</param>
    public void InitializePluginScripts(IScriptApiProvider apiProvider, World world, ILogger logger)
    {
        _logger.Information("Initializing plugin scripts");

        foreach (
            var (modId, scriptTypes) in _pluginScriptTypes
                .GroupBy(kvp => ExtractModIdFromKey(kvp.Key))
                .ToDictionary(g => g.Key, g => g.ToList())
        )
        {
            _pluginScriptsByMod.TryAdd(modId, new List<ScriptBase>());

            foreach (var scriptTypeKvp in scriptTypes)
                try
                {
                    // Use compiled delegate factory (much faster than Activator.CreateInstance)
                    var factory = _compilationCache.FactoryCache.GetOrCreateFactory(
                        scriptTypeKvp.Value
                    );
                    if (factory == null)
                    {
                        var errorMessage =
                            $"Failed to create factory for plugin script '{scriptTypeKvp.Key}': "
                            + "The script type may not have a parameterless constructor.";
                        _logger.Error(
                            "Failed to create factory for plugin script: Type may not have parameterless constructor"
                        );
                        throw new InvalidOperationException(errorMessage);
                    }

                    var instance = factory();
                    if (instance == null)
                    {
                        var errorMessage =
                            $"Failed to create plugin script instance for '{scriptTypeKvp.Key}': "
                            + "Factory returned null. "
                            + "This indicates a compilation issue - script type should inherit from ScriptBase.";
                        _logger.Error(
                            "Failed to create plugin script instance: Factory returned null"
                        );
                        throw new InvalidOperationException(errorMessage);
                    }

                    // Create context with null entity (plugin script)
                    var context = new ScriptContext(
                        world,
                        null,
                        logger,
                        apiProvider,
                        ExtractScriptPathFromKey(scriptTypeKvp.Key),
                        new Dictionary<string, object>()
                    );

                    instance.Initialize(context);
                    instance.RegisterEventHandlers(context);

                    _pluginScriptsByMod[modId].Add(instance);
                    _logger.Debug("Initialized plugin script: {ScriptPath}", scriptTypeKvp.Key);
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Failed to initialize plugin script: {ScriptPath}",
                        scriptTypeKvp.Key
                    );
                }
        }

        _logger.Information(
            "Initialized {PluginScriptCount} plugin scripts",
            _pluginScriptsByMod.Values.Sum(list => list.Count)
        );
    }

    /// <summary>
    ///     Unloads all plugin scripts for a mod.
    /// </summary>
    /// <param name="modId">The mod ID.</param>
    public void UnloadModScripts(string modId)
    {
        if (_pluginScriptsByMod.TryRemove(modId, out var scripts))
        {
            foreach (var script in scripts)
                try
                {
                    script.OnUnload();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error unloading plugin script for mod {ModId}", modId);
                }

            _logger.Debug("Unloaded {Count} plugin scripts for mod {ModId}", scripts.Count, modId);
        }
    }

    /// <summary>
    ///     Loads and compiles a script from a definition.
    /// </summary>
    /// <param name="scriptDef">The script definition.</param>
    /// <param name="dependencyReferences">Pre-resolved dependency references.</param>
    private void LoadScriptFromDefinition(
        ScriptDefinition scriptDef,
        List<MetadataReference> dependencyReferences
    )
    {
        // Get definition metadata to find the original mod
        var definitionMetadata = _registry.GetById(scriptDef.Id);
        if (definitionMetadata == null)
        {
            _logger.Warning(
                "Definition metadata not found for script definition {ScriptId}",
                scriptDef.Id
            );
            return;
        }

        // Get mod manifest using the original mod ID from metadata
        var modManifest = _modManager.GetModManifest(definitionMetadata.OriginalModId);
        if (modManifest == null)
        {
            _logger.Warning(
                "Mod not found for script definition {ScriptId} (mod ID: {ModId})",
                scriptDef.Id,
                definitionMetadata.OriginalModId
            );
            return;
        }

        // Use ResourceManager to load script file (works for both directory and compressed mods)
        string scriptContent;
        try
        {
            scriptContent = _resourceManager.LoadTextFile(scriptDef.Id, scriptDef.ScriptPath);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to load script file for definition {ScriptId}: {ScriptPath}",
                scriptDef.Id,
                scriptDef.ScriptPath
            );
            return;
        }

        // Use pre-resolved dependencies (no need to resolve again)
        try
        {
            var compiledType = _compiler.CompileScriptContent(
                scriptContent,
                scriptDef.ScriptPath,
                dependencyReferences
            );

            // Cache in shared cache
            _compilationCache.TypeCache.CacheCompiledType(scriptDef.Id, compiledType);
            _logger.Debug("Cached script type for definition: {ScriptId}", scriptDef.Id);
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Failed to compile script for definition {ScriptId} from path {ScriptPath}",
                scriptDef.Id,
                scriptDef.ScriptPath
            );
            // Re-throw to fail fast (no fallback)
            throw;
        }
    }

    /// <summary>
    ///     Loads and compiles a plugin script.
    /// </summary>
    private void LoadPluginScript(string modId, string scriptPath)
    {
        // Get mod manifest to resolve dependencies and ModSource
        var modManifest = _modManager.GetModManifest(modId);
        if (modManifest == null)
        {
            _logger.Warning("Mod manifest not found for mod {ModId}", modId);
            return;
        }

        // Plugin scripts don't have definitions, so we use ModSource directly
        // (ResourceManager.LoadTextFile requires a resource ID from a definition)
        if (modManifest.ModSource == null)
        {
            _logger.Warning(
                "ModSource is null for mod {ModId} (plugin script: {ScriptPath})",
                modId,
                scriptPath
            );
            return;
        }

        if (!modManifest.ModSource.FileExists(scriptPath))
        {
            _logger.Warning(
                "Plugin script file not found: {ScriptPath} (mod: {ModId})",
                scriptPath,
                modId
            );
            return;
        }

        string scriptContent;
        try
        {
            scriptContent = modManifest.ModSource.ReadTextFile(scriptPath);
        }
        catch (Exception ex)
        {
            _logger.Warning(
                ex,
                "Failed to load plugin script file: {ScriptPath} (mod: {ModId})",
                scriptPath,
                modId
            );
            return;
        }

        // Resolve dependency assemblies for this mod
        var dependencyReferences = ResolveDependencyAssemblies(modManifest);

        // Compile script content directly (works for compressed mods)
        // CompileScriptContent throws exceptions on failure (fail-fast)
        try
        {
            var compiledType = _compiler.CompileScriptContent(
                scriptContent,
                scriptPath,
                dependencyReferences
            );
            var key = $"{modId}:{scriptPath}";
            _pluginScriptTypes[key] = compiledType;
            _logger.Debug("Cached plugin script type: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.Error(
                ex,
                "Failed to compile plugin script: {ScriptPath} (mod: {ModId})",
                scriptPath,
                modId
            );
            // Re-throw to fail fast (no fallback)
            throw;
        }
    }

    /// <summary>
    ///     Resolves metadata references from mod dependencies.
    ///     Collects all assemblies from mods that the given mod depends on.
    /// </summary>
    /// <param name="modManifest">The mod manifest to resolve dependencies for.</param>
    /// <returns>List of metadata references from dependency mods.</returns>
    private List<MetadataReference> ResolveDependencyAssemblies(ModManifest modManifest)
    {
        var references = new List<MetadataReference>();
        var processedMods = new HashSet<string>(); // Avoid duplicate references

        // Recursively collect assemblies from all dependencies
        CollectDependencyAssemblies(modManifest, references, processedMods);

        if (references.Count > 0)
            _logger.Debug(
                "Resolved {Count} assembly references for mod {ModId}",
                references.Count,
                modManifest.Id
            );

        return references;
    }

    /// <summary>
    ///     Recursively collects assemblies from a mod and its dependencies.
    /// </summary>
    private void CollectDependencyAssemblies(
        ModManifest mod,
        List<MetadataReference> references,
        HashSet<string> processedMods
    )
    {
        // Avoid processing the same mod twice
        if (processedMods.Contains(mod.Id))
            return;
        processedMods.Add(mod.Id);

        // Add assemblies from this mod
        if (mod.Assemblies != null && mod.Assemblies.Count > 0)
        {
            if (mod.ModSource == null)
            {
                _logger.Warning(
                    "ModSource is null for mod {ModId}, cannot load assemblies",
                    mod.Id
                );
                return;
            }

            foreach (var assemblyPath in mod.Assemblies)
            {
                if (!mod.ModSource.FileExists(assemblyPath))
                {
                    _logger.Warning(
                        "Assembly file not found: {AssemblyPath} in mod {ModId}",
                        assemblyPath,
                        mod.Id
                    );
                    continue;
                }

                try
                {
                    // For compressed mods, extract to temp file; for directories, use direct path
                    MetadataReference reference;
                    if (mod.ModSource.IsCompressed)
                    {
                        // Extract assembly to temporary file for MetadataReference.CreateFromFile
                        var assemblyBytes = mod.ModSource.ReadFile(assemblyPath);
                        var tempFile = Path.Combine(
                            Path.GetTempPath(),
                            $"monoball_{mod.Id}_{Path.GetFileName(assemblyPath)}"
                        );
                        File.WriteAllBytes(tempFile, assemblyBytes);

                        // Track temp file for cleanup
                        _compilationCache.TempFileManager.TrackTempFile(mod.Id, tempFile);

                        reference = MetadataReference.CreateFromFile(tempFile);
                    }
                    else
                    {
                        // Directory mods: use direct file path
                        var fullAssemblyPath = Path.Combine(mod.ModSource.SourcePath, assemblyPath);
                        reference = MetadataReference.CreateFromFile(fullAssemblyPath);
                    }

                    references.Add(reference);
                    _logger.Debug(
                        "Added assembly reference: {AssemblyPath} from mod {ModId}",
                        assemblyPath,
                        mod.Id
                    );
                }
                catch (Exception ex)
                {
                    _logger.Warning(
                        ex,
                        "Failed to add assembly reference {AssemblyPath} from mod {ModId}",
                        assemblyPath,
                        mod.Id
                    );
                }
            }
        }

        // Recursively process dependencies
        if (mod.Dependencies != null && mod.Dependencies.Count > 0)
            foreach (var depId in mod.Dependencies)
            {
                var dependency = _modManager.GetModManifest(depId);
                if (dependency != null)
                    CollectDependencyAssemblies(dependency, references, processedMods);
                else
                    _logger.Warning(
                        "Dependency mod {DepId} not found for mod {ModId}",
                        depId,
                        mod.Id
                    );
            }
    }

    /// <summary>
    ///     Parses a plugin script type key into mod ID and script path.
    ///     Key format: "modId:scriptPath"
    /// </summary>
    /// <param name="key">The plugin script type key.</param>
    /// <returns>A tuple containing the mod ID and script path.</returns>
    private (string modId, string scriptPath) ParsePluginScriptKey(string key)
    {
        var colonIndex = key.IndexOf(':');
        if (colonIndex > 0)
            return (key.Substring(0, colonIndex), key.Substring(colonIndex + 1));
        // Fallback: treat entire key as modId (shouldn't happen in normal operation)
        return (key, key);
    }

    /// <summary>
    ///     Extracts mod ID from plugin script type key.
    /// </summary>
    private string ExtractModIdFromKey(string key)
    {
        return ParsePluginScriptKey(key).modId;
    }

    /// <summary>
    ///     Extracts script path from plugin script type key.
    /// </summary>
    private string ExtractScriptPathFromKey(string key)
    {
        return ParsePluginScriptKey(key).scriptPath;
    }

    /// <summary>
    ///     Disposes of all resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose plugin scripts
            foreach (var modScripts in _pluginScriptsByMod.Values)
            foreach (var script in modScripts)
                try
                {
                    script.OnUnload();
                    script.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error disposing plugin script during shutdown");
                }

            _pluginScriptsByMod.Clear();
            _pluginScriptTypes.Clear();

            // NOTE: Do NOT clear or dispose the shared compilation cache
            // It's a shared singleton that persists across ScriptLoaderService instances
            // Temp files are cleaned up by TempFileManager.Dispose() (called when game exits)
        }
    }
}
