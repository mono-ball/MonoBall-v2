# Scripting System Performance Analysis

## Executive Summary

The scripting system has **critical performance bottlenecks** that result in **~3 minutes of startup time** (181 seconds) for 2174 scripts. This analysis identifies **10 major optimization opportunities** that could reduce startup time by **88-94%** and improve runtime performance.

---

## Critical Issues

### 1. ⚠️ **Double Script Pre-Loading** (CRITICAL)
**Impact**: ~181 seconds wasted (102s + 79s)

**Problem**: Scripts are compiled twice during initialization:
- First: Early `SystemManager` in `MonoBallGame.LoadContent()` → ~102 seconds
- Second: Full `SystemManager` in `GameInitializationHelper.InitializeEcsSystems()` → ~79 seconds

**Root Cause**: Each `SystemManager` creates its own `ScriptLoaderService` with separate compilation cache.

**Location**:
- `MonoBallGame.LoadContent()` line 157-165
- `GameInitializationHelper.InitializeEcsSystems()` line 158-166
- `SystemManager.InitializeCoreServices()` line 525

**Solution**: 
- Make `ScriptLoaderService` a singleton or store compiled types in shared location (e.g., `ModManager`)
- Check if script already compiled before compiling
- Reuse early `SystemManager` instead of creating second one

**Expected Impact**: **50% reduction** (181s → 90s)

---

### 2. ⚠️ **Sequential Script Compilation** (CRITICAL)
**Impact**: ~90 seconds for 2174 scripts (sequential)

**Problem**: All scripts compiled one-by-one in `foreach` loop. No parallelization.

**Location**: `ScriptLoaderService.PreloadAllScripts()` line 76-99

```csharp
foreach (var scriptDefId in scriptDefinitionIds)
{
    LoadScriptFromDefinition(scriptDef); // Sequential, blocking
}
```

**Solution**: 
- Use `Parallel.ForEach` with degree of parallelism (4-8 threads)
- Batch scripts by mod to share dependency resolution
- Add progress reporting for better UX

**Expected Impact**: **75-87% reduction** (90s → 11-22s with 4-8 threads)

---

### 3. ⚠️ **Repeated Dependency Resolution** (HIGH)
**Impact**: Significant overhead per script

**Problem**: Each script from the same mod resolves dependencies independently, even though they share the same mod manifest and dependencies.

**Location**: `ScriptLoaderService.LoadScriptFromDefinition()` line 325

```csharp
// Called for EVERY script, even from same mod
var dependencyReferences = ResolveDependencyAssemblies(modManifest);
```

**Solution**: 
- Cache dependency resolution results per mod ID
- Resolve once per mod, reuse for all scripts in that mod

**Expected Impact**: **20-30% reduction** in compilation time per script

---

### 4. ⚠️ **No Shared Compilation Cache** (HIGH)
**Impact**: Duplicate compilation work

**Problem**: Each `ScriptLoaderService` instance has its own `_compiledScriptTypes` dictionary. Compiled types are not shared between instances.

**Location**: `ScriptLoaderService` line 22

```csharp
private readonly ConcurrentDictionary<string, Type> _compiledScriptTypes = new();
```

**Solution**: 
- Make compilation cache static or store in `ModManager`
- Check cache before compiling
- Share cache across all `ScriptLoaderService` instances

**Expected Impact**: Eliminates duplicate compilation (part of issue #1)

---

### 5. ⚠️ **Reflection-Based Instance Creation** (MEDIUM)
**Impact**: Slower script instantiation at runtime

**Problem**: Uses `Activator.CreateInstance()` which is slower than compiled delegates.

**Location**: 
- `ScriptLoaderService.CreateScriptInstance()` line 161
- `ScriptLoaderService.InitializePluginScripts()` line 212

```csharp
var instance = Activator.CreateInstance(scriptType) as ScriptBase;
```

**Solution**: 
- Use `Expression.Lambda<Func<ScriptBase>>()` to compile delegate factories
- Cache compiled delegates per script type
- ~10x faster than `Activator.CreateInstance()`

**Expected Impact**: **90% faster** script instantiation (useful for hot-reload, entity spawning)

---

### 6. ⚠️ **ScriptLifecycleSystem Query Every Frame** (MEDIUM)
**Impact**: Unnecessary work when scripts haven't changed

**Problem**: `ScriptLifecycleSystem.Update()` queries all entities with `ScriptAttachmentComponent` every frame, even when nothing changed.

**Location**: `ScriptLifecycleSystem.Update()` line 95-156

**Solution**: 
- Only query when `ScriptAttachmentComponent` changes (subscribe to component change events)
- Use dirty tracking to skip frames when no changes
- Cache query results when possible

**Expected Impact**: **50-80% reduction** in per-frame overhead when scripts are stable

---

### 7. ⚠️ **Excessive Debug Logging in Hot Paths** (LOW-MEDIUM)
**Impact**: Logging overhead in performance-critical code

**Problem**: Debug logs in every script initialization, compilation, and lifecycle check.

**Location**: 
- `ScriptLoaderService.PreloadAllScripts()` line 78
- `ScriptLifecycleSystem.Update()` line 116-122, 139-152
- `ScriptLifecycleSystem.InitializeScript()` line 224-240

**Solution**: 
- Remove or reduce debug logging in hot paths
- Use structured logging with conditional compilation
- Only log at Information level for important events

**Expected Impact**: **5-10% reduction** in compilation/initialization time

---

### 8. ⚠️ **Temp File Leak for Compressed Mods** (MEDIUM)
**Impact**: Disk space and potential file handle leaks

**Problem**: Creates temp files for compressed mod assemblies but never cleans them up.

**Location**: `ScriptLoaderService.CollectDependencyAssemblies()` line 502-507

```csharp
var tempFile = Path.Combine(
    Path.GetTempPath(),
    $"monoball_{mod.Id}_{Path.GetFileName(assemblyPath)}"
);
File.WriteAllBytes(tempFile, assemblyBytes);
reference = MetadataReference.CreateFromFile(tempFile);
// Never deleted!
```

**Solution**: 
- Track temp files and delete on dispose
- Use `FileOptions.DeleteOnClose` if possible
- Clean up on application exit

**Expected Impact**: Prevents disk space leaks, no performance impact

---

### 9. ⚠️ **Reflection in IsEventForThisEntity** (LOW-MEDIUM)
**Impact**: Reflection overhead on every event check

**Problem**: Uses reflection to get `Entity` property from events, even though it's cached.

**Location**: `ScriptBase.IsEventForThisEntity<TEvent>()` line 220-234

**Solution**: 
- Cache is good, but could use `Expression` trees to compile property getters
- Or use source generators to create optimized property accessors
- Consider making events implement an interface with `Entity` property

**Expected Impact**: **50-70% faster** event entity checks

---

### 10. ⚠️ **No Script Instance Pooling** (LOW)
**Impact**: GC pressure from frequent script instantiation

**Problem**: Creates new script instances every time, even for frequently reused scripts.

**Location**: `ScriptLoaderService.CreateScriptInstance()` line 161

**Solution**: 
- Pool script instances for common scripts (e.g., NPC behaviors)
- Only pool stateless scripts (scripts that don't store entity-specific state)
- Use object pooling pattern

**Expected Impact**: **Reduced GC pressure**, faster entity spawning

---

## Performance Metrics

### Current Performance
- **Startup Time**: ~181 seconds (3 minutes) for 2174 scripts
- **Script Compilation**: ~83ms per script (sequential)
- **Script Instantiation**: ~0.1-0.5ms per script (reflection-based)
- **Script Lifecycle Query**: Every frame (even when unchanged)

### Target Performance (After Optimizations)
- **Startup Time**: ~11-22 seconds (88-94% reduction)
- **Script Compilation**: ~5-10ms per script (parallel, cached dependencies)
- **Script Instantiation**: ~0.01-0.05ms per script (compiled delegates)
- **Script Lifecycle Query**: Only when scripts change

---

## Optimization Priority

### Phase 1: Critical (Immediate Impact)
1. ✅ **Fix double script pre-loading** (#1)
2. ✅ **Parallelize script compilation** (#2)
3. ✅ **Cache dependency resolution** (#3)
4. ✅ **Share compilation cache** (#4)

**Expected Impact**: **88-94% reduction** in startup time (181s → 11-22s)

### Phase 2: High Value (Runtime Performance)
5. ✅ **Compiled delegate factories** (#5)
6. ✅ **Optimize ScriptLifecycleSystem queries** (#6)
7. ✅ **Reduce debug logging** (#7)

**Expected Impact**: **50-80% reduction** in runtime overhead

### Phase 3: Polish (Long-term)
8. ✅ **Fix temp file leaks** (#8)
9. ✅ **Optimize event entity checks** (#9)
10. ✅ **Script instance pooling** (#10)

**Expected Impact**: Better resource management, reduced GC pressure

---

## Implementation Recommendations

### 1. Shared Compilation Cache
```csharp
// In ModManager or static class
public static class ScriptCompilationCache
{
    private static readonly ConcurrentDictionary<string, Type> _compiledTypes = new();
    private static readonly ConcurrentDictionary<string, List<MetadataReference>> _dependencyCache = new();
    
    public static bool TryGetCompiledType(string scriptId, out Type? type)
    {
        return _compiledTypes.TryGetValue(scriptId, out type);
    }
    
    public static void CacheCompiledType(string scriptId, Type type)
    {
        _compiledTypes[scriptId] = type;
    }
    
    public static List<MetadataReference> GetOrResolveDependencies(ModManifest mod, Func<ModManifest, List<MetadataReference>> resolver)
    {
        return _dependencyCache.GetOrAdd(mod.Id, _ => resolver(mod));
    }
}
```

### 2. Parallel Compilation
```csharp
public void PreloadAllScripts()
{
    _logger.Information("Pre-loading all scripts");
    
    var scriptDefinitionIds = _registry.GetByType("Script").ToList();
    var totalScripts = scriptDefinitionIds.Count;
    var processed = 0;
    
    // Group by mod to share dependency resolution
    var scriptsByMod = scriptDefinitionIds
        .GroupBy(id => _registry.GetById(id)?.OriginalModId ?? "unknown")
        .ToList();
    
    var parallelOptions = new ParallelOptions
    {
        MaxDegreeOfParallelism = Environment.ProcessorCount // or 4-8
    };
    
    Parallel.ForEach(scriptsByMod, parallelOptions, modGroup =>
    {
        // Resolve dependencies once per mod
        var modId = modGroup.Key;
        var modManifest = _modManager.GetModManifest(modId);
        var dependencyReferences = modManifest != null 
            ? ScriptCompilationCache.GetOrResolveDependencies(modManifest, ResolveDependencyAssemblies)
            : new List<MetadataReference>();
        
        foreach (var scriptDefId in modGroup)
        {
            // Check cache first
            if (ScriptCompilationCache.TryGetCompiledType(scriptDefId, out var cachedType))
            {
                _compiledScriptTypes[scriptDefId] = cachedType;
                Interlocked.Increment(ref processed);
                continue;
            }
            
            try
            {
                var scriptDef = _registry.GetById<ScriptDefinition>(scriptDefId);
                if (scriptDef != null)
                {
                    LoadScriptFromDefinition(scriptDef, dependencyReferences);
                    Interlocked.Increment(ref processed);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to preload script {ScriptId}", scriptDefId);
            }
        }
    });
    
    _logger.Information("Pre-loaded {Count} scripts", _compiledScriptTypes.Count);
}
```

### 3. Compiled Delegate Factories
```csharp
private static readonly ConcurrentDictionary<Type, Func<ScriptBase>> _factoryCache = new();

public ScriptBase CreateScriptInstance(string definitionId)
{
    if (!_compiledScriptTypes.TryGetValue(definitionId, out var scriptType))
        throw new InvalidOperationException($"Script type not found: {definitionId}");
    
    // Get or create compiled factory
    var factory = _factoryCache.GetOrAdd(scriptType, type =>
    {
        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
            throw new InvalidOperationException($"No parameterless constructor: {type.Name}");
        
        // Compile delegate: () => new ScriptType()
        var newExpr = Expression.New(constructor);
        var lambda = Expression.Lambda<Func<ScriptBase>>(newExpr);
        return lambda.Compile();
    });
    
    return factory();
}
```

### 4. Optimized ScriptLifecycleSystem
```csharp
private bool _scriptsChanged = true; // Track when scripts change

public override void Update(in float deltaTime)
{
    // Only query when scripts have changed
    if (!_scriptsChanged && _previousAttachments.Count > 0)
        return; // Skip this frame
    
    _scriptsChanged = false;
    
    // ... rest of update logic ...
}

// Subscribe to component change events
private void OnScriptAttachmentChanged(ComponentChangedEvent<ScriptAttachmentComponent> evt)
{
    _scriptsChanged = true;
}
```

---

## Testing Recommendations

1. **Benchmark script compilation** with and without parallelization
2. **Measure startup time** before and after optimizations
3. **Profile runtime performance** with script-heavy scenes
4. **Test with various script counts** (100, 1000, 2000+)
5. **Verify correctness** after each optimization phase

---

## Summary

The scripting system has **10 major optimization opportunities** that could reduce startup time from **181 seconds to 11-22 seconds** (88-94% improvement) and significantly improve runtime performance. The most critical issues are:

1. **Double compilation** (50% waste)
2. **Sequential processing** (75-87% improvement potential)
3. **Repeated dependency resolution** (20-30% improvement)
4. **No shared cache** (eliminates duplicates)

Implementing Phase 1 optimizations should provide immediate, dramatic improvements to startup time and user experience.
