# Scripting Performance Design - Architecture & Code Quality Issues

## Overview

This document analyzes the scripting system performance optimization design for architecture issues, SOLID/DRY violations, .cursorrules compliance, code smells, and Arch ECS/Event system issues.

---

## Critical Architecture Issues

### 1. ⚠️ **Static Class Violates Dependency Inversion Principle**

**Issue**: `ScriptCompilationCache` is a static class, making it impossible to:
- Mock for testing
- Inject dependencies
- Replace implementation
- Follow dependency injection patterns

**Location**: Design document section 1.1

**Violation**: SOLID - Dependency Inversion Principle

**Current Design**:
```csharp
public static class ScriptCompilationCache
{
    // Static methods, no dependency injection
}
```

**Problem**: 
- Cannot be tested in isolation
- Cannot be replaced with alternative implementations
- Violates .cursorrules: "Dependency Inversion: Depend on abstractions, not concretions"

**Solution**: Create an interface and instance-based service:

```csharp
/// <summary>
///     Interface for script compilation cache.
/// </summary>
public interface IScriptCompilationCache
{
    bool TryGetCompiledType(string scriptId, out Type? type);
    void CacheCompiledType(string scriptId, Type type);
    List<MetadataReference> GetOrResolveDependencies(
        ModManifest mod,
        Func<ModManifest, List<MetadataReference>> resolver
    );
    Func<ScriptBase> GetOrCreateFactory(Type scriptType);
    void TrackTempFile(string modId, string tempFilePath);
    void CleanupModTempFiles(string modId);
    void Clear();
}

/// <summary>
///     Thread-safe cache for compiled script types and dependency references.
///     Shared across all ScriptLoaderService instances to prevent duplicate compilation.
/// </summary>
public class ScriptCompilationCache : IScriptCompilationCache
{
    // Instance-based, can be injected, tested, and replaced
    private readonly ConcurrentDictionary<string, Type> _compiledTypes = new();
    // ... rest of implementation
}
```

**Registration**: Register as singleton in `Game.Services` or `ModManager`.

---

### 2. ⚠️ **Single Responsibility Violation**

**Issue**: `ScriptCompilationCache` has multiple responsibilities:
1. Caching compiled script types
2. Caching dependency references
3. Compiling delegate factories
4. Tracking temp files
5. Cleaning up temp files

**Violation**: SOLID - Single Responsibility Principle

**Solution**: Split into separate services:

```csharp
/// <summary>
///     Caches compiled script types.
/// </summary>
public interface IScriptTypeCache
{
    bool TryGetCompiledType(string scriptId, out Type? type);
    void CacheCompiledType(string scriptId, Type type);
    void Clear();
}

/// <summary>
///     Caches dependency references per mod.
/// </summary>
public interface IDependencyReferenceCache
{
    List<MetadataReference> GetOrResolveDependencies(
        ModManifest mod,
        Func<ModManifest, List<MetadataReference>> resolver
    );
    void Clear();
}

/// <summary>
///     Compiles and caches delegate factories for script instantiation.
/// </summary>
public interface IScriptFactoryCache
{
    Func<ScriptBase> GetOrCreateFactory(Type scriptType);
    void Clear();
}

/// <summary>
///     Tracks and cleans up temporary files created during script compilation.
/// </summary>
public interface ITempFileManager : IDisposable
{
    void TrackTempFile(string modId, string tempFilePath);
    void CleanupModTempFiles(string modId);
    void CleanupAllTempFiles();
}
```

**Benefits**:
- Each service has single responsibility
- Can be tested independently
- Can be replaced/mocked individually
- Follows SOLID principles

---

### 3. ⚠️ **Empty Catch Block Violates .cursorrules**

**Issue**: Design has empty catch block that silently ignores errors.

**Location**: Design document line 127-130

**Violation**: .cursorrules - "NO FALLBACK CODE" - "Fail fast with clear exceptions"

**Current Design**:
```csharp
catch
{
    // Ignore cleanup errors
}
```

**Problem**: 
- Silently ignores file deletion errors
- No logging of failures
- Could hide serious issues (permissions, disk full, etc.)

**Solution**: Log errors and optionally re-throw:

```csharp
catch (Exception ex)
{
    _logger.Warning(
        ex,
        "Failed to delete temp file during cleanup: {TempFilePath}",
        file
    );
    // Don't re-throw - cleanup failures shouldn't crash the game
    // But log them so we know about the issue
}
```

**Note**: Cleanup errors shouldn't crash the game, but they should be logged.

---

## SOLID Principle Violations

### 4. ⚠️ **Open/Closed Principle Violation**

**Issue**: `ScriptCompilationCache` is not extensible. If we need different caching strategies (e.g., LRU cache, size limits), we'd need to modify the class.

**Solution**: Use strategy pattern or allow configuration:

```csharp
public interface ICacheStrategy<TKey, TValue>
{
    bool TryGet(TKey key, out TValue? value);
    void Set(TKey key, TValue value);
    void Clear();
}

public class ScriptCompilationCache : IScriptCompilationCache
{
    private readonly ICacheStrategy<string, Type> _typeCache;
    private readonly ICacheStrategy<string, List<MetadataReference>> _dependencyCache;
    
    public ScriptCompilationCache(
        ICacheStrategy<string, Type>? typeCache = null,
        ICacheStrategy<string, List<MetadataReference>>? dependencyCache = null
    )
    {
        _typeCache = typeCache ?? new ConcurrentDictionaryCache<string, Type>();
        _dependencyCache = dependencyCache ?? new ConcurrentDictionaryCache<string, List<MetadataReference>>();
    }
}
```

---

### 5. ⚠️ **Interface Segregation Violation**

**Issue**: If we create `IScriptCompilationCache`, clients that only need type caching are forced to depend on temp file management, dependency caching, etc.

**Solution**: Use separate interfaces (as shown in issue #2).

---

## DRY Violations

### 6. ⚠️ **Duplicate Cache Logic**

**Issue**: The design has similar patterns for multiple caches (types, dependencies, factories) that could be abstracted.

**Solution**: Create generic cache wrapper:

```csharp
/// <summary>
///     Generic thread-safe cache wrapper.
/// </summary>
public class ThreadSafeCache<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _cache = new();
    
    public bool TryGet(TKey key, out TValue? value)
    {
        return _cache.TryGetValue(key, out value);
    }
    
    public void Set(TKey key, TValue value)
    {
        _cache[key] = value;
    }
    
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        return _cache.GetOrAdd(key, factory);
    }
    
    public void Clear()
    {
        _cache.Clear();
    }
}
```

---

## .cursorrules Compliance Issues

### 7. ⚠️ **Missing XML Documentation**

**Issue**: Design shows methods without XML documentation comments.

**Violation**: .cursorrules - "Document all public APIs with XML comments"

**Solution**: Add full XML documentation:

```csharp
/// <summary>
///     Gets a compiled script type from cache, or null if not found.
/// </summary>
/// <param name="scriptId">The script definition ID.</param>
/// <param name="type">When this method returns, contains the compiled type if found; otherwise, null.</param>
/// <returns>True if the type was found in cache, false otherwise.</returns>
public bool TryGetCompiledType(string scriptId, out Type? type)
{
    return _compiledTypes.TryGetValue(scriptId, out type);
}
```

---

### 8. ⚠️ **Missing Null Checks**

**Issue**: Design doesn't show null validation for parameters.

**Violation**: .cursorrules - "Validate arguments and throw ArgumentNullException"

**Solution**: Add null checks:

```csharp
public void CacheCompiledType(string scriptId, Type type)
{
    if (string.IsNullOrWhiteSpace(scriptId))
        throw new ArgumentException("Script ID cannot be null or empty.", nameof(scriptId));
    if (type == null)
        throw new ArgumentNullException(nameof(type));
    
    _compiledTypes[scriptId] = type;
}
```

---

### 9. ⚠️ **Exception Handling Not Documented**

**Issue**: Methods that throw exceptions don't document them in XML comments.

**Violation**: .cursorrules - "Document exceptions in XML comments using `<exception>` tags"

**Solution**: Add exception documentation:

```csharp
/// <summary>
///     Gets or creates a compiled delegate factory for a script type.
/// </summary>
/// <param name="scriptType">The script type to create a factory for.</param>
/// <returns>A compiled delegate factory that creates instances of the script type.</returns>
/// <exception cref="ArgumentNullException">Thrown when scriptType is null.</exception>
/// <exception cref="InvalidOperationException">Thrown when scriptType doesn't have a parameterless constructor.</exception>
public Func<ScriptBase> GetOrCreateFactory(Type scriptType)
{
    if (scriptType == null)
        throw new ArgumentNullException(nameof(scriptType));
    
    return _factoryCache.GetOrAdd(scriptType, CreateFactory);
}
```

---

## Code Smells

### 10. ⚠️ **Locking on List Inside ConcurrentDictionary**

**Issue**: Design locks on a `List<string>` that's stored in a `ConcurrentDictionary`, which can cause deadlocks.

**Location**: Design document line 102-105

**Current Design**:
```csharp
_tempFiles.AddOrUpdate(
    modId,
    new List<string> { tempFilePath },
    (key, existing) =>
    {
        lock (existing)  // ⚠️ Locking on mutable object in dictionary
        {
            existing.Add(tempFilePath);
        }
        return existing;
    }
);
```

**Problem**:
- Locking on objects stored in collections is an anti-pattern
- Can cause deadlocks if the list is accessed elsewhere
- `ConcurrentDictionary` already provides thread safety

**Solution**: Use `ConcurrentBag` or `ConcurrentQueue`:

```csharp
// Use ConcurrentBag for thread-safe collection
private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> _tempFiles = new();

public void TrackTempFile(string modId, string tempFilePath)
{
    var bag = _tempFiles.GetOrAdd(modId, _ => new ConcurrentBag<string>());
    bag.Add(tempFilePath);
}

public void CleanupModTempFiles(string modId)
{
    if (_tempFiles.TryRemove(modId, out var files))
    {
        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to delete temp file: {TempFilePath}", file);
            }
        }
    }
}
```

---

### 11. ⚠️ **Version Tracking Defeats Purpose**

**Issue**: `GetComponentVersion()` still queries all entities every frame, which defeats the optimization purpose.

**Location**: Design document section 4, line 411-428

**Current Design**:
```csharp
private int GetComponentVersion()
{
    var hash = 0;
    World.Query(  // ⚠️ Still queries every frame!
        in _queryDescription,
        (Entity entity, ref ScriptAttachmentComponent component) =>
        {
            hash ^= entity.Id.GetHashCode();
            // ...
        }
    );
    return hash;
}
```

**Problem**: 
- Still performs full query every frame
- Only saves work if hash matches (rare)
- Hash collision could cause missed updates

**Better Solution**: Subscribe to component change events or use Arch ECS change tracking:

```csharp
public class ScriptLifecycleSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private bool _needsUpdate = true; // Set to true initially
    
    public override void Update(in float deltaTime)
    {
        if (!_needsUpdate && _previousAttachments.Count > 0)
        {
            return; // Skip if no changes
        }
        
        _needsUpdate = false;
        // ... existing update logic ...
    }
    
    // Subscribe to events that indicate script changes
    private void OnScriptAttachmentChanged(ref ScriptAttachmentChangedEvent evt)
    {
        _needsUpdate = true;
    }
    
    // Or subscribe to EntityCreated/EntityDestroyed events
    private void OnEntityCreated(ref EntityCreatedEvent evt)
    {
        if (World.Has<ScriptAttachmentComponent>(evt.Entity))
            _needsUpdate = true;
    }
}
```

**Alternative**: If Arch ECS doesn't support change events, use a dirty flag that's set when scripts are added/removed:

```csharp
// In MapLoaderSystem or wherever scripts are attached
World.Set(entity, scriptComponent);
ScriptLifecycleSystem.MarkDirty(); // Static method or event
```

---

### 12. ⚠️ **Missing Method in Design**

**Issue**: Design references `ScriptCompilationCache.GetCompiledTypeCount()` but doesn't define it.

**Location**: Design document line 280

**Solution**: Add the method:

```csharp
/// <summary>
///     Gets the number of compiled script types in the cache.
/// </summary>
/// <returns>The number of cached script types.</returns>
public int GetCompiledTypeCount()
{
    return _compiledTypes.Count;
}
```

---

## Arch ECS / Event System Issues

### 13. ⚠️ **No Component Change Events**

**Issue**: Design assumes component change events exist, but Arch ECS may not support them.

**Problem**: The version tracking approach queries every frame anyway, and there's no clear way to detect component changes without querying.

**Solution Options**:

**Option A**: Use events when scripts are attached/detached:
```csharp
// When attaching script
World.Set(entity, scriptComponent);
var evt = new ScriptAttachmentChangedEvent { Entity = entity };
EventBus.Send(ref evt);

// In ScriptLifecycleSystem
_subscriptions.Add(EventBus.Subscribe<ScriptAttachmentChangedEvent>(OnScriptChanged));
```

**Option B**: Use a dirty flag system:
```csharp
public static class ScriptChangeTracker
{
    private static volatile bool _isDirty = true;
    
    public static void MarkDirty() => _isDirty = true;
    public static bool IsDirty() => _isDirty;
    public static void MarkClean() => _isDirty = false;
}

// In ScriptLifecycleSystem
if (!ScriptChangeTracker.IsDirty() && _previousAttachments.Count > 0)
    return;
    
ScriptChangeTracker.MarkClean();
```

**Option C**: Accept that we query every frame but optimize the query itself (use smaller query, cache results, etc.)

---

### 14. ⚠️ **Event Entity Property Access Pattern**

**Issue**: The `IsEventForThisEntity` optimization uses reflection/expression trees, but events might not always have an `Entity` property.

**Problem**: 
- Not all events have `Entity` property
- Some events use different property names (`InteractionEntity`, `ShaderEntity`, etc.)
- The optimization assumes a consistent pattern

**Solution**: Make it more flexible:

```csharp
private static Func<object, Entity?>? CreateEntityPropertyGetter(Type eventType)
{
    // Try common property names
    var propertyNames = new[] { "Entity", "InteractionEntity", "ShaderEntity", "TargetEntity" };
    
    foreach (var propName in propertyNames)
    {
        var entityProp = eventType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        if (entityProp != null && entityProp.PropertyType == typeof(Entity))
        {
            // Found matching property, compile getter
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
```

---

## Thread Safety Issues

### 15. ⚠️ **Race Condition in Temp File Cleanup**

**Issue**: `CleanupAllTempFiles()` iterates over `_tempFiles.Keys.ToList()` which creates a snapshot, but files could be added between snapshot and cleanup.

**Problem**: Files added during cleanup iteration might not be cleaned up.

**Solution**: Use atomic operations or lock:

```csharp
public void CleanupAllTempFiles()
{
    // Get all mod IDs atomically
    var allMods = new List<string>();
    foreach (var kvp in _tempFiles)
        allMods.Add(kvp.Key);
    
    // Cleanup each mod (new files added during cleanup will be in dictionary)
    foreach (var modId in allMods)
        CleanupModTempFiles(modId);
    
    // Final pass: cleanup any remaining files
    while (!_tempFiles.IsEmpty)
    {
        var remaining = _tempFiles.Keys.ToList();
        foreach (var modId in remaining)
            CleanupModTempFiles(modId);
    }
}
```

---

## Missing Error Handling

### 16. ⚠️ **Factory Compilation Errors Not Handled**

**Issue**: `CreateFactory()` can throw exceptions, but they're not caught in `GetOrCreateFactory()`.

**Problem**: If factory compilation fails, the exception propagates and could crash.

**Solution**: Add error handling and caching of failures:

```csharp
private static Func<ScriptBase>? CreateFactory(Type scriptType)
{
    try
    {
        var constructor = scriptType.GetConstructor(Type.EmptyTypes);
        if (constructor == null)
        {
            _logger.Error(
                "Script type {ScriptType} does not have a parameterless constructor.",
                scriptType.Name
            );
            return null;
        }
        
        var newExpr = Expression.New(constructor);
        var lambda = Expression.Lambda<Func<ScriptBase>>(newExpr);
        return lambda.Compile();
    }
    catch (Exception ex)
    {
        _logger.Error(
            ex,
            "Failed to create factory for script type {ScriptType}",
            scriptType.Name
        );
        return null;
    }
}

public Func<ScriptBase>? GetOrCreateFactory(Type scriptType)
{
    if (scriptType == null)
        throw new ArgumentNullException(nameof(scriptType));
    
    return _factoryCache.GetOrAdd(scriptType, CreateFactory);
}
```

**Note**: Return type becomes nullable, need to handle null in `CreateScriptInstance()`.

---

## Summary of Required Changes

### High Priority (Architecture)

1. ✅ Replace static class with interface + instance service
2. ✅ Split `ScriptCompilationCache` into separate services (SRP)
3. ✅ Fix empty catch block (add logging)
4. ✅ Fix locking anti-pattern (use `ConcurrentBag`)

### Medium Priority (Code Quality)

5. ✅ Add XML documentation for all public methods
6. ✅ Add null checks and argument validation
7. ✅ Document exceptions in XML comments
8. ✅ Fix version tracking (use events or dirty flags)
9. ✅ Add missing `GetCompiledTypeCount()` method

### Low Priority (Polish)

10. ✅ Make event entity property detection more flexible
11. ✅ Improve temp file cleanup race condition handling
12. ✅ Add error handling for factory compilation
13. ✅ Consider cache strategy pattern for extensibility

---

## Recommended Refactored Architecture

```csharp
// Interfaces (abstractions)
public interface IScriptTypeCache { ... }
public interface IDependencyReferenceCache { ... }
public interface IScriptFactoryCache { ... }
public interface ITempFileManager : IDisposable { ... }

// Implementations
public class ScriptTypeCache : IScriptTypeCache { ... }
public class DependencyReferenceCache : IDependencyReferenceCache { ... }
public class ScriptFactoryCache : IScriptFactoryCache { ... }
public class TempFileManager : ITempFileManager { ... }

// Composite service (or use DI container)
public class ScriptCompilationCache : IScriptCompilationCache
{
    private readonly IScriptTypeCache _typeCache;
    private readonly IDependencyReferenceCache _dependencyCache;
    private readonly IScriptFactoryCache _factoryCache;
    private readonly ITempFileManager _tempFileManager;
    
    public ScriptCompilationCache(
        IScriptTypeCache typeCache,
        IDependencyReferenceCache dependencyCache,
        IScriptFactoryCache factoryCache,
        ITempFileManager tempFileManager
    )
    {
        _typeCache = typeCache ?? throw new ArgumentNullException(nameof(typeCache));
        // ... etc
    }
}
```

**Registration**:
```csharp
// In Game.Services or ModManager
var typeCache = new ScriptTypeCache();
var dependencyCache = new DependencyReferenceCache();
var factoryCache = new ScriptFactoryCache();
var tempFileManager = new TempFileManager(logger);
var compilationCache = new ScriptCompilationCache(
    typeCache,
    dependencyCache,
    factoryCache,
    tempFileManager
);

Services.AddService(typeof(IScriptCompilationCache), compilationCache);
```

This approach:
- ✅ Follows SOLID principles
- ✅ Allows testing and mocking
- ✅ Follows .cursorrules
- ✅ Eliminates code smells
- ✅ Maintains thread safety
- ✅ Is extensible and maintainable
