# Scripting System Performance Optimization Design

## Overview

This document provides a comprehensive design for optimizing the scripting system performance, targeting **88-94% reduction in startup time** (from ~181s to ~11-22s) and significant runtime performance improvements.

---

## ⚠️ Critical Findings Summary

The following issues were discovered during code analysis and are **critical to address first**:

### 1. Double SystemManager Creation (50% of startup time wasted)

**Location**: `MonoBallGame.cs:157` and `GameInitializationHelper.cs:158`

**Issue**: The game creates two SystemManagers:
- **Early SystemManager** - for loading screen display
- **Async SystemManager** - for full game initialization

Each calls `PreloadAllScripts()` (`SystemManager.cs:493`), causing **all scripts to be compiled twice**.

**Impact**: ~90 seconds wasted on duplicate compilation (50% of 181s startup)

**Fix**: See [Section 6: Double SystemManager Creation Fix](#6-double-systemmanager-creation-fix-critical-finding)

### 2. Cache Must Be Registered Before First SystemManager

**Issue**: `IScriptCompilationCache` must be created and registered in `Game.Services` **before** any SystemManager is created, including the early one.

**Fix**: See [Section 8: Early Cache Registration](#8-early-cache-registration)

### 3. ScriptLifecycleSystem Queries Every Frame

**Location**: `ScriptLifecycleSystem.cs`

**Issue**: The system queries all entities with `ScriptAttachmentComponent` every frame, even when no scripts have been added/removed.

**Impact**: Unnecessary per-frame overhead

**Fix**: See [Section 4: Modified ScriptLifecycleSystem](#4-modified-scriptlifecyclesystem) - uses dirty flag pattern

---

## Table of Contents

1. [Architecture Changes](#architecture-changes)
   - 1.1 [Shared Compilation Cache](#1-shared-compilation-cache-refactored-for-solid-compliance)
   - 1.2 [Modified ScriptLoaderService](#2-modified-scriptloaderservice)
   - 1.3 [Modified ScriptCompilerService](#3-modified-scriptcompilerservice)
   - 1.4 [Modified ScriptLifecycleSystem](#4-modified-scriptlifecyclesystem)
   - 1.5 [Optimized ScriptBase.IsEventForThisEntity](#5-optimized-scriptbaseiseventforthisentity)
   - 1.6 [Double SystemManager Creation Fix](#6-double-systemmanager-creation-fix-critical-finding) ⚠️ **CRITICAL**
   - 1.7 [SystemManager Initialization Order Fix](#7-systemmanager-initialization-order-fix)
   - 1.8 [Early Cache Registration](#8-early-cache-registration)
2. [Phase 1: Critical Optimizations](#phase-1-critical-optimizations)
3. [Phase 2: Runtime Optimizations](#phase-2-runtime-optimizations)
4. [Phase 3: Polish & Resource Management](#phase-3-polish--resource-management)
5. [Implementation Plan](#implementation-plan)
6. [Thread Safety Considerations](#thread-safety-considerations)
7. [Migration Path](#migration-path)
8. [Testing Strategy](#testing-strategy)

---

## Architecture Changes

### 1. Shared Compilation Cache (Refactored for SOLID Compliance)

**Problem**: Each `ScriptLoaderService` instance has its own compilation cache, causing duplicate compilation.

**Solution**: Create instance-based, thread-safe compilation cache services that follow SOLID principles and can be injected/tested.

#### 1.1 Interface-Based Architecture

**Location**: `MonoBall.Core.Scripting.Services`

**Design**: Split into separate interfaces and implementations following Single Responsibility Principle:

```csharp
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

/// <summary>
///     Tracks and cleans up temporary files created during script compilation.
/// </summary>
public interface ITempFileManager : IDisposable
{
    /// <summary>
    ///     Tracks a temp file for cleanup.
    /// </summary>
    /// <param name="modId">The mod ID that owns the temp file.</param>
    /// <param name="tempFilePath">The path to the temp file.</param>
    /// <exception cref="ArgumentException">Thrown when modId or tempFilePath is null or empty.</exception>
    void TrackTempFile(string modId, string tempFilePath);
    
    /// <summary>
    ///     Cleans up all temp files for a mod.
    /// </summary>
    /// <param name="modId">The mod ID to clean up temp files for.</param>
    void CleanupModTempFiles(string modId);
    
    /// <summary>
    ///     Cleans up all tracked temp files.
    /// </summary>
    void CleanupAllTempFiles();
}

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
```

#### 1.2 Implementations

**Location**: `MonoBall.Core.Scripting.Services`

```csharp
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
        
        return _dependencyCache.GetOrAdd(mod.Id, _ =>
        {
            _logger?.Debug("Resolving dependencies for mod: {ModId}", mod.Id);
            var references = resolver(mod);
            _logger?.Debug("Resolved {Count} dependency references for mod: {ModId}", references.Count, mod.Id);
            return references;
        });
    }
    
    /// <inheritdoc />
    public void Clear()
    {
        _dependencyCache.Clear();
        _logger?.Debug("Cleared dependency reference cache");
    }
}

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

/// <summary>
///     Thread-safe manager for temporary files created during script compilation.
/// </summary>
public class TempFileManager : ITempFileManager
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _tempFiles = new();
    private readonly ILogger? _logger;
    private bool _disposed;
    
    /// <summary>
    ///     Initializes a new instance of the TempFileManager class.
    /// </summary>
    /// <param name="logger">Optional logger for debugging.</param>
    public TempFileManager(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    /// <inheritdoc />
    public void TrackTempFile(string modId, string tempFilePath)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or empty.", nameof(modId));
        if (string.IsNullOrWhiteSpace(tempFilePath))
            throw new ArgumentException("Temp file path cannot be null or empty.", nameof(tempFilePath));
        
        var bag = _tempFiles.GetOrAdd(modId, _ => new ConcurrentBag<string>());
        bag.Add(tempFilePath);
        _logger?.Debug("Tracking temp file for mod {ModId}: {TempFilePath}", modId, tempFilePath);
    }
    
    /// <inheritdoc />
    public void CleanupModTempFiles(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("Mod ID cannot be null or empty.", nameof(modId));
        
        if (_tempFiles.TryRemove(modId, out var files))
        {
            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        _logger?.Debug("Deleted temp file: {TempFilePath}", file);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        ex,
                        "Failed to delete temp file during cleanup: {TempFilePath}",
                        file
                    );
                    // Don't re-throw - cleanup failures shouldn't crash the game
                }
            }
        }
    }
    
    /// <inheritdoc />
    public void CleanupAllTempFiles()
    {
        // Get all mod IDs atomically
        var allMods = new List<string>();
        foreach (var kvp in _tempFiles)
            allMods.Add(kvp.Key);
        
        // Cleanup each mod
        foreach (var modId in allMods)
            CleanupModTempFiles(modId);
        
        // Final pass: cleanup any remaining files added during iteration
        while (!_tempFiles.IsEmpty)
        {
            var remaining = _tempFiles.Keys.ToList();
            if (remaining.Count == 0)
                break;
                
            foreach (var modId in remaining)
                CleanupModTempFiles(modId);
        }
    }
    
    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupAllTempFiles();
            _tempFiles.Clear();
            _disposed = true;
        }
    }
}

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
        DependencyCache = dependencyCache ?? throw new ArgumentNullException(nameof(dependencyCache));
        FactoryCache = factoryCache ?? throw new ArgumentNullException(nameof(factoryCache));
        TempFileManager = tempFileManager ?? throw new ArgumentNullException(nameof(tempFileManager));
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
```

**Thread Safety**: All implementations use `ConcurrentDictionary` and `ConcurrentBag` for thread-safe access.

---

### 2. Modified `ScriptLoaderService`

**Changes**:
- Inject `IScriptCompilationCache` instead of using static class
- Check cache before compiling
- Use cached dependency resolution
- Track temp files for cleanup

**Key Method Changes**:

```csharp
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
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _compilationCache = compilationCache ?? throw new ArgumentNullException(nameof(compilationCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    ///     Pre-loads all scripts during mod loading phase.
    ///     Compiles and caches script types (not instances).
    ///     Plugin scripts are compiled but NOT initialized here.
    /// </summary>
    public void PreloadAllScripts()
    {
        _logger.Information("Pre-loading all scripts");
        
        var scriptDefinitionIds = _registry.GetByType("Script").ToList();
        var totalScripts = scriptDefinitionIds.Count;
        
        // Group scripts by mod to share dependency resolution
        var scriptsByMod = scriptDefinitionIds
            .GroupBy(id =>
            {
                var metadata = _registry.GetById(id);
                return metadata?.OriginalModId ?? "unknown";
            })
            .ToList();
        
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) // Cap at 8 threads
        };
        
        var processedCount = 0;
        var lockObj = new object();
        
        Parallel.ForEach(scriptsByMod, parallelOptions, modGroup =>
        {
            var modId = modGroup.Key;
            var modManifest = _modManager.GetModManifest(modId);
            
            if (modManifest == null)
            {
                _logger.Warning("Mod manifest not found for mod {ModId}", modId);
                return;
            }
            
            // Resolve dependencies once per mod (cached)
            var dependencyReferences = _compilationCache.DependencyCache.GetOrResolveDependencies(
                modManifest,
                ResolveDependencyAssemblies
            );
            
            foreach (var scriptDefId in modGroup)
            {
                // Check cache first
                if (_compilationCache.TypeCache.TryGetCompiledType(scriptDefId, out _))
                {
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
                }
            }
        });
        
        _logger.Information(
            "Pre-loaded {Count} entity-attached scripts and {PluginCount} plugin scripts",
            _compilationCache.TypeCache.GetCompiledTypeCount(),
            _pluginScriptTypes.Count
        );
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
        
        // Use ResourceManager to load script file
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
            throw; // Re-throw to fail fast
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
        
        // Get from shared cache
        if (!_compilationCache.TypeCache.TryGetCompiledType(definitionId, out var scriptType))
        {
            throw new InvalidOperationException(
                $"Script type not found in cache for definition '{definitionId}'. " +
                "Ensure the script was pre-loaded during mod loading phase."
            );
        }
        
        // Use compiled delegate factory (much faster than Activator.CreateInstance)
        var factory = _compilationCache.FactoryCache.GetOrCreateFactory(scriptType);
        if (factory == null)
        {
            throw new InvalidOperationException(
                $"Failed to create factory for script type '{scriptType.Name}'. " +
                "The script type may not have a parameterless constructor."
            );
        }
        
        return factory();
    }
    
    /// <summary>
    ///     Resolves metadata references from mod dependencies.
    /// </summary>
    /// <param name="modManifest">The mod manifest to resolve dependencies for.</param>
    /// <returns>List of metadata references from dependency mods.</returns>
    private List<MetadataReference> ResolveDependencyAssemblies(ModManifest modManifest)
    {
        var references = new List<MetadataReference>();
        var processedMods = new HashSet<string>();
        
        CollectDependencyAssemblies(modManifest, references, processedMods);
        
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
        {
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
    }
    
    /// <summary>
    ///     Disposes of all resources.
    /// </summary>
    public void Dispose()
    {
        // Cleanup plugin scripts
        // Note: Don't clear shared cache - it's shared across instances
        // Temp files are cleaned up by ModManager disposal or TempFileManager.Dispose()
    }
}
```

**Service Registration**: Register `IScriptCompilationCache` as singleton in `Game.Services` or `ModManager`:

```csharp
// In ModManager or GameInitializationHelper
var typeCache = new ScriptTypeCache(logger);
var dependencyCache = new DependencyReferenceCache(logger);
var factoryCache = new ScriptFactoryCache(logger);
var tempFileManager = new TempFileManager(logger);
var compilationCache = new ScriptCompilationCache(
    typeCache,
    dependencyCache,
    factoryCache,
    tempFileManager
);

game.Services.AddService(typeof(IScriptCompilationCache), compilationCache);
```

---

### 3. Modified `ScriptCompilerService`

**Changes**: No changes needed - already thread-safe for parallel compilation.

**Note**: Roslyn compilation is thread-safe, so multiple scripts can be compiled in parallel.

---

### 4. Modified `ScriptLifecycleSystem`

**Problem**: Queries every frame even when scripts haven't changed.

**Solution**: Use event-based change detection or dirty flag system to avoid querying when unchanged.

**Design**: Use dirty flag that's set when scripts are attached/detached:

```csharp
/// <summary>
///     Tracks when script attachments change to optimize ScriptLifecycleSystem queries.
/// </summary>
public static class ScriptChangeTracker
{
    private static volatile bool _isDirty = true; // Start dirty to ensure initial query
    
    /// <summary>
    ///     Marks that script attachments have changed and need processing.
    /// </summary>
    public static void MarkDirty()
    {
        _isDirty = true;
    }
    
    /// <summary>
    ///     Checks if script attachments have changed since last check.
    /// </summary>
    /// <returns>True if dirty, false if clean.</returns>
    public static bool IsDirty()
    {
        return _isDirty;
    }
    
    /// <summary>
    ///     Marks script attachments as clean (no changes).
    /// </summary>
    public static void MarkClean()
    {
        _isDirty = false;
    }
}

public class ScriptLifecycleSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly IScriptApiProvider _apiProvider;
    private readonly HashSet<(Entity Entity, string ScriptDefinitionId)> _initializedScripts = new();
    private readonly ILogger _logger;
    private readonly HashSet<(Entity Entity, string ScriptDefinitionId)> _previousAttachments = new();
    private readonly QueryDescription _queryDescription;
    private readonly DefinitionRegistry _registry;
    private readonly Dictionary<(Entity Entity, string ScriptDefinitionId), ScriptBase> _scriptInstances = new();
    private readonly ScriptLoaderService _scriptLoader;
    private readonly List<IDisposable> _subscriptions = new();
    private bool _disposed;
    
    /// <summary>
    ///     Initializes a new instance of the ScriptLifecycleSystem class.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="scriptLoader">The script loader service.</param>
    /// <param name="apiProvider">The script API provider.</param>
    /// <param name="registry">The definition registry.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    public ScriptLifecycleSystem(
        World world,
        ScriptLoaderService scriptLoader,
        IScriptApiProvider apiProvider,
        DefinitionRegistry registry,
        ILogger logger
    )
        : base(world)
    {
        _scriptLoader = scriptLoader ?? throw new ArgumentNullException(nameof(scriptLoader));
        _apiProvider = apiProvider ?? throw new ArgumentNullException(nameof(apiProvider));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Cache QueryDescription in constructor
        _queryDescription = new QueryDescription().WithAll<ScriptAttachmentComponent>();
        
        // Subscribe to entity events to detect script changes
        _subscriptions.Add(EventBus.Subscribe<EntityCreatedEvent>(OnEntityCreated));
        _subscriptions.Add(EventBus.Subscribe<EntityDestroyedEvent>(OnEntityDestroyed));
        
        // Mark dirty initially to ensure we process existing entities on first update
        // (entities created before this system was initialized won't trigger EntityCreatedEvent)
        ScriptChangeTracker.MarkDirty();
        
        // Note: Component changes are detected when scripts are attached/detached
        // Systems that attach scripts should call ScriptChangeTracker.MarkDirty()
    }
    
    /// <summary>
    ///     Updates script lifecycle: initializes new scripts, cleans up removed ones.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    public override void Update(in float deltaTime)
    {
        // Only query if scripts have changed
        if (!ScriptChangeTracker.IsDirty() && _previousAttachments.Count > 0)
        {
            return; // Skip this frame - no changes
        }
        
        ScriptChangeTracker.MarkClean(); // Mark as processed
        
        var currentAttachments = new HashSet<(Entity Entity, string ScriptDefinitionId)>();
        
        // Query entities with ScriptAttachmentComponent
        World.Query(
            in _queryDescription,
            (Entity entity, ref ScriptAttachmentComponent component) =>
            {
                // Ensure entity is still alive
                if (!World.IsAlive(entity))
                    return;
                
                // Ensure Scripts dictionary is initialized
                if (component.Scripts == null)
                    component.Scripts = new Dictionary<string, ScriptAttachmentData>();
                
                // Iterate over all scripts in the collection
                foreach (var kvp in component.Scripts)
                {
                    var scriptDefinitionId = kvp.Key;
                    var attachment = kvp.Value;
                    
                    if (!attachment.IsActive)
                        continue; // Skip inactive scripts
                    
                    var key = (entity, scriptDefinitionId);
                    currentAttachments.Add(key);
                    
                    // Check if script needs initialization
                    if (!_initializedScripts.Contains(key))
                    {
                        InitializeScript(entity, attachment);
                    }
                }
            }
        );
        
        // Cleanup scripts that were removed
        var scriptsToRemove = new List<(Entity Entity, string ScriptDefinitionId)>();
        foreach (var key in _previousAttachments)
            if (!currentAttachments.Contains(key))
                scriptsToRemove.Add(key);
        
        foreach (var key in scriptsToRemove)
            CleanupScript(key.Entity, key.ScriptDefinitionId);
        
        // Update previous attachments for next frame
        _previousAttachments.Clear();
        foreach (var key in currentAttachments)
            _previousAttachments.Add(key);
    }
    
    /// <summary>
    ///     Handles entity creation - marks dirty if entity has scripts.
    /// </summary>
    private void OnEntityCreated(EntityCreatedEvent evt)
    {
        if (World.Has<ScriptAttachmentComponent>(evt.Entity))
            ScriptChangeTracker.MarkDirty();
    }
    
    /// <summary>
    ///     Handles entity destruction - cleans up scripts and marks dirty.
    /// </summary>
    private void OnEntityDestroyed(EntityDestroyedEvent evt)
    {
        var scriptsToRemove = new List<(Entity Entity, string ScriptDefinitionId)>();
        foreach (var key in _scriptInstances.Keys)
            if (key.Entity.Id == evt.Entity.Id)
                scriptsToRemove.Add(key);
        
        foreach (var key in scriptsToRemove)
            CleanupScript(key.Entity, key.ScriptDefinitionId);
        
        if (scriptsToRemove.Count > 0)
            ScriptChangeTracker.MarkDirty();
    }
    
    // ... rest of existing methods (InitializeScript, CleanupScript, etc.) ...
}
```

**Note on ScriptChangeTracker**: 
- `ScriptChangeTracker` is a static class (similar to `EventBus`)
- This is acceptable for simple infrastructure utilities
- If testing becomes an issue, can be refactored to interface + instance later
- Thread-safe using `volatile bool` with atomic operations

**Systems that attach/detach scripts MUST call `ScriptChangeTracker.MarkDirty()`**:

```csharp
// In MapLoaderSystem or wherever scripts are attached
World.Set(entity, scriptComponent);
ScriptChangeTracker.MarkDirty(); // Notify that scripts changed
```

**Required Call Sites** (implemented):

| Location | When Called | Purpose |
|----------|-------------|---------|
| `ScriptLifecycleSystem` constructor | System startup | Ensure initial processing of pre-existing entities |
| `ScriptLifecycleSystem.OnEntityDestroyed` | Scripts cleaned up | Script removal changed state |
| `MapLoaderSystem.LoadNpc` | NPC created with scripts | Consolidated call after entity creation if scripts attached |
| `ScriptAttachmentHelper` | Any script modification | All helper methods automatically call MarkDirty() |

**Automatic Handling** - The following are handled automatically:
- Entity creation with scripts → `MapLoaderSystem` calls `MarkDirty()` after creating entities with scripts
- `EntityDestroyedEvent` → auto cleans up and marks dirty
- Using `ScriptAttachmentHelper` methods → auto marks dirty

**Reset Method** - Call `ScriptChangeTracker.Reset()` during:
- Scene transitions (to re-process all scripts)
- Unit tests (to reset static state)

**Helper Methods** (in `MonoBall.Core.ECS.Utilities.ScriptAttachmentHelper`):

```csharp
// Set script attachment with automatic dirty marking
ScriptAttachmentHelper.SetScriptAttachment(world, entity, scriptId, data);

// Pause/Resume scripts (for interactions) with automatic dirty marking
ScriptAttachmentHelper.PauseScript(world, entity, scriptDefinitionId);
ScriptAttachmentHelper.ResumeScript(world, entity, scriptDefinitionId);

// Generic active state change
ScriptAttachmentHelper.SetScriptActive(world, entity, scriptDefinitionId, isActive);
```

**Do NOT call MarkDirty() directly** - Always use the helper methods to ensure consistency.

---

### 5. Optimized `ScriptBase.IsEventForThisEntity`

**Problem**: Uses reflection to get `Entity` property, even though cached. Not all events use the same property name.

**Solution**: Use compiled expression trees for property access, with support for multiple property names.

**Design**:

```csharp
public abstract class ScriptBase : IDisposable
{
    // Cache compiled property getters: eventType -> Func<object, Entity?>
    private static readonly ConcurrentDictionary<Type, Func<object, Entity?>?> _entityPropertyGetters = new();
    
    // Common property names for entity references in events
    private static readonly string[] EntityPropertyNames = new[]
    {
        "Entity",
        "InteractionEntity",
        "ShaderEntity",
        "TargetEntity",
        "SourceEntity"
    };
    
    /// <summary>
    ///     Checks if an event belongs to this script's entity.
    ///     Uses compiled expression trees for fast property access.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="evt">The event to check.</param>
    /// <returns>True if the event belongs to this entity, false otherwise.</returns>
    protected bool IsEventForThisEntity<TEvent>(TEvent evt)
        where TEvent : struct
    {
        if (!Context.Entity.HasValue)
            return false;
        
        var eventType = typeof(TEvent);
        var getter = _entityPropertyGetters.GetOrAdd(eventType, CreateEntityPropertyGetter);
        
        if (getter == null)
            return false; // Event doesn't have an entity property
        
        object boxedEvt = evt;
        var eventEntity = getter(boxedEvt);
        return eventEntity.HasValue && eventEntity.Value.Id == Context.Entity.Value.Id;
    }
    
    /// <summary>
    ///     Checks if an event (passed by ref) belongs to this script's entity.
    ///     Uses compiled expression trees for fast property access.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="evt">The event to check (passed by ref).</param>
    /// <returns>True if the event belongs to this entity, false otherwise.</returns>
    protected bool IsEventForThisEntity<TEvent>(ref TEvent evt)
        where TEvent : struct
    {
        if (!Context.Entity.HasValue)
            return false;
        
        // Copy struct for property access (can't use reflection directly on ref parameters)
        var evtCopy = evt;
        return IsEventForThisEntity(evtCopy);
    }
    
    /// <summary>
    ///     Creates a compiled property getter for an event type.
    ///     Tries common property names (Entity, InteractionEntity, etc.).
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>A compiled getter function, or null if no entity property found.</returns>
    private static Func<object, Entity?>? CreateEntityPropertyGetter(Type eventType)
    {
        // Try common property names
        foreach (var propName in EntityPropertyNames)
        {
            var entityProp = eventType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
            if (entityProp != null && entityProp.PropertyType == typeof(Entity))
            {
                // Found matching property, compile getter
                // Compile: (object evt) => (Entity?)evt.PropertyName
                var param = Expression.Parameter(typeof(object), "evt");
                var cast = Expression.Convert(param, eventType);
                var prop = Expression.Property(cast, entityProp);
                var convert = Expression.Convert(prop, typeof(Entity?));
                var lambda = Expression.Lambda<Func<object, Entity?>>(convert, param);
                return lambda.Compile();
            }
        }
        
        return null; // No entity property found
    }
}
```

---

### 6. Double SystemManager Creation Fix (Critical Finding)

**Problem**: The game creates **two SystemManager instances** during startup:

1. **Early SystemManager** (`MonoBallGame.cs:157`) - Created for the loading screen
2. **Async SystemManager** (`GameInitializationHelper.cs:158`) - Created during async initialization

Each `SystemManager.Initialize()` calls `_scriptLoaderService.PreloadAllScripts()` (`SystemManager.cs:493`), causing **all scripts to be compiled twice**. This doubles the startup time unnecessarily.

**Root Cause Analysis**:

```
MonoBallGame.Initialize()
├── new SystemManager() [Early - line 157]
│   └── Initialize()
│       └── PreloadAllScripts() ← First compilation (all scripts)
│
└── GameInitializationService.CreateLoadingSceneAndStartInitialization()
    └── GameInitializationHelper.CreateSystemManager()
        └── new SystemManager() [Async - line 158]
            └── Initialize()
                └── PreloadAllScripts() ← Second compilation (duplicate!)
```

**Solution**: Modify `PreloadAllScripts()` to skip compilation if scripts are already cached.

**Design**:

```csharp
// In ScriptLoaderService.cs
public void PreloadAllScripts()
{
    _logger.Information("Pre-loading all scripts");
    
    var scriptDefinitionIds = _registry.GetByType("Script").ToList();
    var totalScripts = scriptDefinitionIds.Count;
    
    if (totalScripts == 0)
    {
        _logger.Information("No scripts to preload");
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
            return;
        }
    }
    catch (Exception ex)
    {
        _logger.Warning(
            ex,
            "Error checking script cache, proceeding with full preload"
        );
        // Continue with full preload - don't fail fast for cache check errors
    }
    
    // Group scripts by mod to share dependency resolution
    var scriptsByMod = scriptDefinitionIds
        .GroupBy(id =>
        {
            var metadata = _registry.GetById(id);
            return metadata?.OriginalModId ?? "unknown";
        })
        .ToList();
    
    var parallelOptions = new ParallelOptions
    {
        MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8)
    };
    
    var processedCount = 0;
    var cachedCount = 0;
    
    Parallel.ForEach(scriptsByMod, parallelOptions, modGroup =>
    {
        // ... mod setup ...
        
        foreach (var scriptDefId in modGroup)
        {
            // Check cache first (handles race conditions - each script checks individually)
            if (_compilationCache.TypeCache.TryGetCompiledType(scriptDefId, out _))
            {
                Interlocked.Increment(ref cachedCount);
                Interlocked.Increment(ref processedCount);
                continue; // Already compiled
            }
            
            // ... compile script ...
            Interlocked.Increment(ref processedCount);
        }
    });
    
    _logger.Information(
        "Pre-loaded {Count} scripts ({Cached} from cache, {Compiled} compiled)",
        processedCount,
        cachedCount,
        processedCount - cachedCount
    );
}
```

**Alternative Solution**: Share the `IScriptCompilationCache` singleton so both SystemManagers use the same cache. The early SystemManager compiles scripts once, and the async SystemManager finds them already cached.

**Files to modify**:
- `MonoBall.Core/Scripting/Services/ScriptLoaderService.cs` - Add cache check at start of `PreloadAllScripts()`
- `MonoBall.Core/GameInitializationHelper.cs` - Ensure singleton cache is used

**Expected Impact**:
- **Startup time**: 50% reduction just from eliminating duplicate compilation
- **Memory**: Reduced peak memory usage (no duplicate assemblies in memory)

---

### 7. SystemManager Initialization Order Fix

**Problem**: The early SystemManager is disposed after the async SystemManager completes (`MonoBallGame.cs:299-310`), but if they share the same `IScriptCompilationCache`, disposal could clear the cache.

**Solution**:
1. Don't dispose the cache when disposing SystemManager - the cache is a singleton
2. OR don't dispose the early SystemManager until game exit

**Design**:

```csharp
// In SystemManager.Dispose()
public void Dispose()
{
    if (_disposed)
        return;

    _disposed = true;

    // Dispose systems
    foreach (var system in _systems)
    {
        if (system is IDisposable disposable)
            disposable.Dispose();
    }

    // Clear event subscriptions
    foreach (var subscription in _subscriptions)
        subscription.Dispose();

    // NOTE: Do NOT dispose or clear the compilation cache
    // It's a shared singleton that should persist across SystemManager instances
    // The cache is disposed by Game.Services when the game exits

    _logger.Debug("SystemManager disposed");
}
```

**Service Lifetime**:

```csharp
// In Game initialization (before any SystemManager creation)
// Register as singleton - created once, shared by all SystemManagers
var compilationCache = CreateScriptCompilationCache(logger);
game.Services.AddService(typeof(IScriptCompilationCache), compilationCache);

// In Game.UnloadContent() or Dispose()
// Cleanup the cache only when the game exits
var cache = Services.GetService<IScriptCompilationCache>();
if (cache is IDisposable disposable)
    disposable.Dispose();
```

---

### 8. Early Cache Registration

**Problem**: The `IScriptCompilationCache` must be registered before **any** SystemManager is created, including the early one for the loading screen.

**Current Flow** (problematic):
```
MonoBallGame.Initialize()
├── LoadModsSynchronously()
├── new SystemManager() [Early] ← Cache doesn't exist yet!
└── GameInitializationService (async)
    └── Creates cache and registers it ← Too late!
```

**Fixed Flow**:
```
MonoBallGame.Initialize()
├── LoadModsSynchronously()
├── CreateAndRegisterCompilationCache() ← Register FIRST
├── new SystemManager() [Early] ← Uses shared cache
└── GameInitializationService (async)
    └── new SystemManager() ← Uses same shared cache
```

**Design**:

```csharp
// In MonoBallGame.LoadContent(), AFTER LoadModsSynchronously() but BEFORE creating early SystemManager
protected override void LoadContent()
{
    base.LoadContent();
    
    _logger.Information("Starting async content loading");
    
    // Load all mods synchronously first for system-critical resources (fonts, etc.)
    LoadModsSynchronously();
    
    // Create main world early (empty, just for scenes)
    var mainWorld = EcsWorld.Instance;
    _logger.Debug("Main world created early for loading scene");
    
    // Get required services
    var modManager = Services.GetService<ModManager>();
    if (modManager == null)
        throw new InvalidOperationException(
            "ModManager not found in Game.Services after LoadModsSynchronously()"
        );
    
    var resourceManager = Services.GetService<IResourceManager>();
    if (resourceManager == null)
        throw new InvalidOperationException(
            "ResourceManager not found in Game.Services. Ensure LoadModsSynchronously() created it."
        );
    
    // CREATE AND REGISTER COMPILATION CACHE BEFORE ANY SYSTEMMANAGER
    var compilationCacheLogger = LoggerFactory.CreateLogger("ScriptCompilationCache");
    var compilationCache = new ScriptCompilationCache(
        new ScriptTypeCache(compilationCacheLogger),
        new DependencyReferenceCache(compilationCacheLogger),
        new ScriptFactoryCache(compilationCacheLogger),
        new TempFileManager(compilationCacheLogger)
    );
    Services.AddService(typeof(IScriptCompilationCache), compilationCache);
    _logger.Debug("Registered IScriptCompilationCache singleton");
    
    // Create sprite batch for early systems
    var loadingSpriteBatch = new SpriteBatch(GraphicsDevice);
    
    // NOW create early SystemManager (will use the shared cache)
    var earlySystemManager = new SystemManager(
        mainWorld,
        GraphicsDevice,
        modManager,
        resourceManager,
        this,  // game reference to get cache from Services
        LoggerFactory.CreateLogger<SystemManager>()
    );
    earlySystemManager.Initialize(loadingSpriteBatch);
    
    // ... rest of initialization ...
}
```

**Note**: Cache registration must be in `LoadContent()`, not `Initialize()`, because:
- `LoadModsSynchronously()` is called in `LoadContent()` (line 124)
- Early SystemManager is created in `LoadContent()` (line 157)
- `Initialize()` doesn't have access to mods or resources yet

**Files to modify**:
- `MonoBall.Core/MonoBallGame.cs` - Register cache in `LoadContent()` before creating early SystemManager
- `MonoBall.Core/ECS/SystemManager.cs` - Get cache from `Game.Services` instead of creating new one

**Helper Method** (for DRY - extract cache creation):

```csharp
// In GameInitializationHelper.cs or MonoBallGame.cs
/// <summary>
///     Creates and registers the script compilation cache as a singleton in Game.Services.
///     Must be called before creating any SystemManager.
/// </summary>
/// <param name="game">The game instance to register the cache with.</param>
/// <param name="logger">The logger for logging operations.</param>
/// <returns>The created compilation cache instance.</returns>
/// <exception cref="ArgumentNullException">Thrown when game is null.</exception>
public static IScriptCompilationCache CreateAndRegisterCompilationCache(
    Game game,
    ILogger logger
)
{
    if (game == null)
        throw new ArgumentNullException(nameof(game));
    if (logger == null)
        throw new ArgumentNullException(nameof(logger));
    
    var compilationCacheLogger = LoggerFactory.CreateLogger("ScriptCompilationCache");
    var compilationCache = new ScriptCompilationCache(
        new ScriptTypeCache(compilationCacheLogger),
        new DependencyReferenceCache(compilationCacheLogger),
        new ScriptFactoryCache(compilationCacheLogger),
        new TempFileManager(compilationCacheLogger)
    );
    
    game.Services.AddService(typeof(IScriptCompilationCache), compilationCache);
    logger.Debug("Registered IScriptCompilationCache singleton");
    
    return compilationCache;
}
```

**Usage**:
```csharp
// In MonoBallGame.LoadContent()
var compilationCache = GameInitializationHelper.CreateAndRegisterCompilationCache(this, _logger);
```

---

## Phase 1: Critical Optimizations

### Implementation Order

1. **Create `ScriptCompilationCache`** services
   - Thread-safe caches (`IScriptTypeCache`, `IDependencyReferenceCache`, `IScriptFactoryCache`)
   - Temp file tracking (`ITempFileManager`)
   - Factory compilation
   - Composite `IScriptCompilationCache` interface

2. **Register cache singleton EARLY in MonoBallGame.Initialize()**
   - Create and register `IScriptCompilationCache` before any SystemManager
   - Ensure both early and async SystemManagers use the same cache

3. **Modify `ScriptLoaderService`**
   - Inject `IScriptCompilationCache` via constructor
   - Check cache before compiling (skip if already cached)
   - Parallel compilation with `Parallel.ForEach`
   - Cached dependency resolution per mod

4. **Update `SystemManager`**
   - Get `IScriptCompilationCache` from `Game.Services`
   - Don't dispose the cache on SystemManager disposal
   - Pass cache to `ScriptLoaderService` constructor

5. **Add progress reporting**
   - Report compilation progress for better UX

### Expected Impact

- **Startup time**: 181s → 11-22s (88-94% reduction)
- **Compilation time**: 83ms/script → 5-10ms/script (parallel)
- **Dependency resolution**: Eliminated duplicate work
- **Double compilation**: Eliminated (50% immediate improvement)

---

## Phase 2: Runtime Optimizations

### Implementation Order

1. **Optimize `ScriptLifecycleSystem`**
   - Version tracking or event subscription
   - Skip frames when unchanged

2. **Use compiled delegate factories**
   - Replace `Activator.CreateInstance`
   - 10x faster instantiation

3. **Optimize `ScriptBase.IsEventForThisEntity`**
   - Compiled expression trees
   - 50-70% faster checks

4. **Reduce debug logging**
   - Remove logs from hot paths
   - Use conditional compilation

### Expected Impact

- **Runtime overhead**: 50-80% reduction
- **Script instantiation**: 0.1-0.5ms → 0.01-0.05ms
- **Event checks**: 50-70% faster

---

## Phase 3: Polish & Resource Management

### Implementation Order

1. **Temp file cleanup**
   - Track temp files in cache
   - Cleanup on mod unload/dispose

2. **Script instance pooling** (optional)
   - Pool stateless scripts
   - Reduce GC pressure

3. **Memory optimization**
   - Clear caches on mod unload
   - Dispose unused resources

### Expected Impact

- **Resource management**: No leaks
- **GC pressure**: Reduced allocations

---

## Implementation Plan

### Step 1: Create Cache Services

**Files to create**:
- `MonoBall.Core/Scripting/Services/IScriptTypeCache.cs`
- `MonoBall.Core/Scripting/Services/ScriptTypeCache.cs`
- `MonoBall.Core/Scripting/Services/IDependencyReferenceCache.cs`
- `MonoBall.Core/Scripting/Services/DependencyReferenceCache.cs`
- `MonoBall.Core/Scripting/Services/IScriptFactoryCache.cs`
- `MonoBall.Core/Scripting/Services/ScriptFactoryCache.cs`
- `MonoBall.Core/Scripting/Services/ITempFileManager.cs`
- `MonoBall.Core/Scripting/Services/TempFileManager.cs`
- `MonoBall.Core/Scripting/Services/IScriptCompilationCache.cs`
- `MonoBall.Core/Scripting/Services/ScriptCompilationCache.cs`
- `MonoBall.Core/ECS/ScriptChangeTracker.cs`

**Dependencies**:
- `ModManager` for service registration
- `ILogger` for logging

**Testing**: Unit tests for cache operations, thread safety, temp file management

---

### Step 1.5: Register Cache Singleton Early (CRITICAL)

**Files to modify**:
- `MonoBall.Core/MonoBallGame.cs`

**Changes**:
- Create and register `IScriptCompilationCache` singleton BEFORE creating early SystemManager
- This ensures both early and async SystemManagers share the same cache

**Code Location**: In `LoadContent()`, after `LoadModsSynchronously()` but BEFORE `new SystemManager()`:

**Important**: Cache registration must be in `LoadContent()`, not `Initialize()`, because:
- `LoadModsSynchronously()` is called in `LoadContent()` (line 124)
- Early SystemManager is created in `LoadContent()` (line 157)
- `Initialize()` doesn't have access to mods or resources yet

```csharp
// BEFORE any SystemManager creation
var compilationCacheLogger = LoggerFactory.CreateLogger("ScriptCompilationCache");
var compilationCache = new ScriptCompilationCache(
    new ScriptTypeCache(compilationCacheLogger),
    new DependencyReferenceCache(compilationCacheLogger),
    new ScriptFactoryCache(compilationCacheLogger),
    new TempFileManager(compilationCacheLogger)
);
Services.AddService(typeof(IScriptCompilationCache), compilationCache);
```

**Testing**:
- Verify cache is registered before first SystemManager creation
- Verify both SystemManagers get the same cache instance
- Verify scripts are only compiled once (not twice)

---

### Step 2: Modify `ScriptLoaderService`

**Files to modify**:
- `MonoBall.Core/Scripting/Services/ScriptLoaderService.cs`

**Changes**:
- Add `IScriptCompilationCache` parameter to constructor
- Remove instance-level `_compiledScriptTypes`
- Use injected `IScriptCompilationCache` for compiled types
- Implement parallel compilation in `PreloadAllScripts()`
- Cache dependency resolution using `IDependencyReferenceCache`
- Use compiled delegate factories from `IScriptFactoryCache` in `CreateScriptInstance()`
- Track temp files using `ITempFileManager`

**Files to modify for service registration**:
- `MonoBall.Core/Mods/ModManager.cs` or `MonoBall.Core/GameInitializationHelper.cs`
- Register `IScriptCompilationCache` as singleton in `Game.Services`

**Testing**: 
- Verify parallel compilation works
- Verify cache sharing works across multiple `ScriptLoaderService` instances
- Verify no duplicate compilation
- Verify dependency resolution is cached per mod
- Verify temp files are tracked and cleaned up

---

### Step 3: Modify `ScriptLifecycleSystem`

**Files to modify**:
- `MonoBall.Core/ECS/Systems/ScriptLifecycleSystem.cs`

**Changes**:
- Use `ScriptChangeTracker` instead of version tracking
- Skip frames when `ScriptChangeTracker.IsDirty()` is false
- Subscribe to `EntityCreatedEvent` and `EntityDestroyedEvent` to mark dirty
- Reduce debug logging in hot paths

**Files to modify for dirty tracking**:
- `MonoBall.Core/ECS/Systems/MapLoaderSystem.cs` (or wherever scripts are attached)
- Call `ScriptChangeTracker.MarkDirty()` when scripts are attached/detached

**Testing**:
- Verify scripts still initialize correctly
- Verify frames are skipped when no changes
- Verify dirty flag is set when scripts are attached/detached
- Verify performance improvement (50-80% reduction in overhead)

---

### Step 4: Optimize `ScriptBase`

**Files to modify**:
- `MonoBall.Core/Scripting/Runtime/ScriptBase.cs`

**Changes**:
- Use compiled expression trees for `IsEventForThisEntity`
- Reduce reflection overhead

**Testing**:
- Verify event filtering still works
- Benchmark performance improvement

---

### Step 5: Update `SystemManager`

**Files to modify**:
- `MonoBall.Core/ECS/SystemManager.cs`

**Changes**:
1. Get `IScriptCompilationCache` from `Game.Services` instead of creating a new instance
2. Pass the cache to `ScriptLoaderService` constructor
3. Don't dispose or clear the cache when SystemManager is disposed
4. The cache check in `PreloadAllScripts()` will automatically skip if scripts are already compiled

**Constructor Change**:

**Note**: `SystemManager` constructor already has `Game game` parameter (line 114). Only need to:
1. Store `Game` reference (already stored as `_game`)
2. Get cache from `Game.Services` in `InitializeCoreServices()`
3. Pass cache to `ScriptLoaderService` constructor

```csharp
// SystemManager constructor already has Game parameter - no changes needed
public SystemManager(
    World world,
    GraphicsDevice graphicsDevice,
    IModManager modManager,
    IResourceManager resourceManager,
    Game game,  // ✅ Already exists (line 114)
    ILogger logger
)
{
    // ... existing code stores _game ...
}

// In InitializeCoreServices() method
private void InitializeCoreServices()
{
    // ... existing code ...
    
    // Get shared cache from Game.Services
    var compilationCache = _game.Services.GetService<IScriptCompilationCache>();
    if (compilationCache == null)
    {
        throw new InvalidOperationException(
            "IScriptCompilationCache not registered in Game.Services. " +
            "Ensure the cache is registered before creating SystemManager."
        );
    }
    
    // Create ScriptLoaderService with shared cache
    _scriptLoaderService = new ScriptLoaderService(
        _scriptCompilerService,
        _modManager.Registry,
        (ModManager)_modManager,
        _resourceManager,
        compilationCache,  // Pass the shared cache
        LoggerFactory.CreateLogger<ScriptLoaderService>()
    );
    
    // Preload all scripts (compiles but doesn't initialize plugin scripts)
    _scriptLoaderService.PreloadAllScripts();
}
```

**Dispose Change**:

```csharp
public void Dispose()
{
    if (_disposed)
        return;

    _disposed = true;

    // Dispose systems
    foreach (var system in _systems)
    {
        if (system is IDisposable disposable)
            disposable.Dispose();
    }

    // NOTE: Do NOT dispose or clear _scriptLoaderService's compilation cache
    // It's a shared singleton that persists across SystemManager instances
    // The ScriptLoaderService.Dispose() should NOT clear the shared cache

    _logger.Debug("SystemManager disposed");
}
```

**Testing**:
- Verify cache is retrieved from Game.Services correctly
- Verify no double pre-loading (scripts compiled only once)
- Verify early SystemManager disposal doesn't affect async SystemManager's scripts
- Verify startup time improvement (~50% from eliminating double compilation)

---

### Step 6: Add Progress Reporting

**Files to modify**:
- `MonoBall.Core/Scripting/Services/ScriptLoaderService.cs`
- `MonoBall.Core/GameInitializationService.cs` (if needed)

**Changes**:
- Report compilation progress
- Update loading screen progress

**Testing**:
- Verify progress updates correctly

---

## Thread Safety Considerations

### Cache Services

- **Thread-safe**: All cache implementations use `ConcurrentDictionary` and `ConcurrentBag`
- **Temp file tracking**: Uses `ConcurrentBag` (no locking needed)
- **Factory compilation**: Thread-safe (Expression.Compile is thread-safe)
- **Dependency resolution**: Cached per mod, thread-safe via `ConcurrentDictionary`

### `ScriptLoaderService.PreloadAllScripts()`

- **Parallel compilation**: Roslyn compilation is thread-safe
- **Cache writes**: `ConcurrentDictionary` handles concurrent writes
- **Progress tracking**: Uses `Interlocked` for atomic increments

### `ScriptLifecycleSystem`

- **Version tracking**: Single-threaded (ECS update loop)
- **Component access**: Arch ECS handles thread safety

---

## Migration Path

### Backward Compatibility

- **No breaking changes**: All optimizations are internal
- **API unchanged**: Public APIs remain the same
- **Behavior unchanged**: Scripts work exactly the same

### Rollout Strategy

1. **Phase 1**: Implement critical optimizations
   - Test thoroughly
   - Benchmark before/after
   - Deploy

2. **Phase 2**: Implement runtime optimizations
   - Test runtime performance
   - Verify correctness
   - Deploy

3. **Phase 3**: Polish and resource management
   - Test resource cleanup
   - Verify no leaks
   - Deploy

### Rollback Plan

- All changes are additive (new classes, modified internals)
- Can revert to old implementation if issues arise
- Cache can be cleared if needed

---

## Testing Strategy

### Unit Tests

1. **`ScriptCompilationCache`**
   - Cache operations (get, set, clear)
   - Thread safety (concurrent access)
   - Temp file tracking
   - Factory compilation

2. **`ScriptLoaderService`**
   - Parallel compilation
   - Cache sharing
   - Dependency resolution caching
   - Delegate factory usage

3. **`ScriptLifecycleSystem`**
   - Version tracking
   - Skip logic
   - Script initialization

### Integration Tests

1. **Startup performance**
   - Measure startup time before/after
   - Verify no duplicate compilation
   - Verify cache sharing works

2. **Runtime performance**
   - Measure script instantiation time
   - Measure event check performance
   - Measure lifecycle system overhead

3. **Correctness**
   - Verify all scripts still work
   - Verify event handling still works
   - Verify hot-reload still works

### Benchmark Tests

1. **Compilation performance**
   - Sequential vs parallel
   - With/without cache
   - With/without dependency caching

2. **Runtime performance**
   - Script instantiation (Activator vs delegate)
   - Event checks (reflection vs expression)
   - Lifecycle system (every frame vs version tracking)

### Performance Targets

- **Startup time**: < 25 seconds (from 181s)
- **Compilation**: < 10ms per script (from 83ms)
- **Instantiation**: < 0.1ms per script (from 0.1-0.5ms)
- **Lifecycle overhead**: < 1ms per frame (when unchanged)

---

## Risk Assessment

### Low Risk

- **Cache sharing**: Well-tested pattern (`ConcurrentDictionary`)
- **Parallel compilation**: Roslyn is thread-safe
- **Delegate factories**: Standard .NET pattern

### Medium Risk

- **Version tracking**: New approach, needs thorough testing
- **Temp file cleanup**: Need to ensure all paths cleanup

### Mitigation

- **Comprehensive testing**: Unit, integration, and benchmark tests
- **Gradual rollout**: Phase by phase
- **Rollback plan**: Can revert if issues arise
- **Monitoring**: Track performance metrics

---

## Success Criteria

### Phase 1 (Critical)

- ✅ Startup time reduced by 80%+ (181s → < 36s)
- ✅ No duplicate compilation
- ✅ Parallel compilation working
- ✅ All tests passing

### Phase 2 (Runtime)

- ✅ Script instantiation 10x faster
- ✅ Lifecycle system overhead reduced by 50%+
- ✅ Event checks 50%+ faster
- ✅ All tests passing

### Phase 3 (Polish)

- ✅ No temp file leaks
- ✅ Memory usage stable
- ✅ All tests passing

---

## Conclusion

This design provides a comprehensive plan for optimizing the scripting system performance. The phased approach allows for incremental improvements with thorough testing at each stage. The expected impact is **88-94% reduction in startup time** and **50-80% reduction in runtime overhead**, significantly improving user experience.
