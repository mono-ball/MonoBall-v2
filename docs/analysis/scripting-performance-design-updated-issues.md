# Scripting Performance Design - Updated Analysis

## Overview

This document analyzes the updated design document for architecture issues, SOLID/DRY violations, .cursorrules compliance, code smells, and Arch ECS/Event system issues after the critical findings were added.

---

## Critical Issues Found

### 1. ⚠️ **Wrong Method for Cache Registration**

**Issue**: Design says to register cache in `MonoBallGame.Initialize()`, but `LoadModsSynchronously()` is called in `LoadContent()`, not `Initialize()`.

**Location**: Design document section 8, line 1384-1415

**Current Design**:
```csharp
// In MonoBallGame.Initialize(), BEFORE creating early SystemManager
protected override void Initialize()
{
    base.Initialize();
    
    LoadModsSynchronously();  // ❌ This is in LoadContent(), not Initialize()!
    
    // CREATE AND REGISTER COMPILATION CACHE BEFORE ANY SYSTEMMANAGER
    var compilationCache = new ScriptCompilationCache(...);
    Services.AddService(typeof(IScriptCompilationCache), compilationCache);
    
    // NOW create early SystemManager
    var earlySystemManager = new SystemManager(...);
}
```

**Problem**: 
- `LoadModsSynchronously()` is called in `LoadContent()` (line 124)
- Early SystemManager is created in `LoadContent()` (line 157)
- Cache registration must be in `LoadContent()`, not `Initialize()`

**Solution**: Register cache in `LoadContent()` after `LoadModsSynchronously()` but before creating SystemManager:

```csharp
// In MonoBallGame.LoadContent()
protected override void LoadContent()
{
    base.LoadContent();
    
    _logger.Information("Starting async content loading");
    
    // Load all mods synchronously first
    LoadModsSynchronously();
    
    // ... existing setup ...
    
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
    
    // NOW create early SystemManager (will use the shared cache)
    var earlySystemManager = new SystemManager(
        mainWorld,
        GraphicsDevice,
        modManager,
        resourceManager,
        this,  // game reference
        LoggerFactory.CreateLogger<SystemManager>()
    );
    earlySystemManager.Initialize(loadingSpriteBatch);
    
    // ... rest of initialization ...
}
```

---

### 2. ⚠️ **ScriptChangeTracker Static Class Violates Dependency Inversion**

**Issue**: `ScriptChangeTracker` is a static class, making it impossible to test, mock, or replace.

**Location**: Design document section 4

**Violation**: SOLID - Dependency Inversion Principle

**Current Design**:
```csharp
public static class ScriptChangeTracker
{
    private static volatile bool _isDirty = true;
    
    public static void MarkDirty() => _isDirty = true;
    public static bool IsDirty() => _isDirty;
    public static void MarkClean() => _isDirty = false;
}
```

**Problem**:
- Cannot be tested in isolation
- Cannot be mocked
- Violates dependency injection principles
- However, it's much simpler than the cache, so this might be acceptable

**Solution Options**:

**Option A**: Keep static (acceptable for simple flag):
- Simple, no dependencies
- Thread-safe with `volatile`
- Acceptable for infrastructure-level utilities

**Option B**: Make it an interface + instance (more testable):
```csharp
public interface IScriptChangeTracker
{
    void MarkDirty();
    bool IsDirty();
    void MarkClean();
}

public class ScriptChangeTracker : IScriptChangeTracker
{
    private volatile bool _isDirty = true;
    
    public void MarkDirty() => _isDirty = true;
    public bool IsDirty() => _isDirty;
    public void MarkClean() => _isDirty = false;
}
```

**Recommendation**: **Option A** - Keep static for now. It's a simple infrastructure utility (like `EventBus`), and the added complexity of DI isn't worth it for a boolean flag. If testing becomes an issue, refactor to Option B.

---

### 3. ⚠️ **PreloadAllScripts Early Return Logic Issue**

**Issue**: The early return check iterates through all scripts to count cached ones, which is O(n) work that could be avoided.

**Location**: Design document section 6, line 1257-1287

**Current Design**:
```csharp
public void PreloadAllScripts()
{
    var scriptDefinitionIds = _registry.GetByType("Script").ToList();
    var totalScripts = scriptDefinitionIds.Count;
    var cachedCount = 0;
    
    // Check how many are already cached
    foreach (var scriptDefId in scriptDefinitionIds)
    {
        if (_compilationCache.TypeCache.TryGetCompiledType(scriptDefId, out _))
            cachedCount++;
    }
    
    // If all scripts are already cached, skip preloading entirely
    if (cachedCount == totalScripts && totalScripts > 0)
    {
        _logger.Information("All {Count} scripts already cached, skipping preload", totalScripts);
        return;
    }
    
    // ... rest of parallel compilation ...
}
```

**Problem**: 
- Iterates through all scripts twice (once for counting, once for compilation)
- Could optimize by checking during the parallel loop instead

**Solution**: Check cache during parallel compilation, skip early return check:

```csharp
public void PreloadAllScripts()
{
    _logger.Information("Pre-loading all scripts");
    
    var scriptDefinitionIds = _registry.GetByType("Script").ToList();
    
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
            // Check cache first (this is the optimization - skip if cached)
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

**Note**: The early return check is still useful for logging, but not necessary for correctness. The parallel loop already handles cached scripts efficiently.

---

### 4. ⚠️ **Missing ScriptChangeTracker.MarkDirty() Calls**

**Issue**: Design says systems should call `ScriptChangeTracker.MarkDirty()` when scripts are attached, but doesn't specify where or how.

**Location**: Design document section 4, line 1119-1125

**Problem**: 
- No clear specification of which systems need to call `MarkDirty()`
- Could be missed during implementation
- Need to identify all places where `ScriptAttachmentComponent` is modified

**Solution**: Document all locations where scripts are attached/detached:

```csharp
// In MapLoaderSystem - when loading map entities with scripts
World.Set(entity, scriptAttachmentComponent);
ScriptChangeTracker.MarkDirty(); // Scripts attached

// In any system that adds/removes ScriptAttachmentComponent
if (World.Has<ScriptAttachmentComponent>(entity))
{
    ref var component = ref World.Get<ScriptAttachmentComponent>(entity);
    component.Scripts[scriptId] = attachmentData;
    ScriptChangeTracker.MarkDirty(); // Script modified
}

// When removing scripts
if (World.Has<ScriptAttachmentComponent>(entity))
{
    ref var component = ref World.Get<ScriptAttachmentComponent>(entity);
    component.Scripts.Remove(scriptId);
    ScriptChangeTracker.MarkDirty(); // Script removed
}
```

**Files to modify**:
- `MonoBall.Core/ECS/Systems/MapLoaderSystem.cs` - Mark dirty when attaching scripts to entities
- Any other system that modifies `ScriptAttachmentComponent`

---

### 5. ⚠️ **SystemManager Constructor Already Has Game Parameter**

**Issue**: Design shows adding `Game` parameter to SystemManager constructor, but it already exists.

**Location**: Design document section 5, line 1646-1676

**Current Reality**: `SystemManager` constructor already has `Game game` parameter (line 114 in SystemManager.cs)

**Solution**: Update design to reflect that `Game` parameter already exists. Only need to:
1. Get cache from `Game.Services` in constructor
2. Pass to `ScriptLoaderService`

**Corrected Design**:
```csharp
// SystemManager constructor already has Game parameter
public SystemManager(
    World world,
    GraphicsDevice graphicsDevice,
    IModManager modManager,
    IResourceManager resourceManager,
    Game game,  // ✅ Already exists
    ILogger logger
)
{
    // ... existing code ...
    
    // Get shared cache from Game.Services
    var compilationCache = game.Services.GetService<IScriptCompilationCache>();
    if (compilationCache == null)
    {
        throw new InvalidOperationException(
            "IScriptCompilationCache not registered in Game.Services. " +
            "Ensure the cache is registered before creating SystemManager."
        );
    }
    
    // Store for later use in InitializeCoreServices()
    _compilationCache = compilationCache;
}

// In InitializeCoreServices()
private void InitializeCoreServices()
{
    // ... existing code ...
    
    // Create ScriptLoaderService with shared cache
    _scriptLoaderService = new ScriptLoaderService(
        _scriptCompilerService,
        _modManager.Registry,
        (ModManager)_modManager,
        _resourceManager,
        _compilationCache,  // Use shared cache
        LoggerFactory.CreateLogger<ScriptLoaderService>()
    );
}
```

---

### 6. ⚠️ **Race Condition in PreloadAllScripts Early Return**

**Issue**: If scripts are being compiled in parallel while another SystemManager calls `PreloadAllScripts()`, the early return check might see partial state.

**Location**: Design document section 6

**Problem**: 
- Thread 1: Early SystemManager starts compiling scripts
- Thread 2: Async SystemManager calls `PreloadAllScripts()` 
- Thread 2's early return check might see some scripts cached, some not
- Could cause incorrect early return

**Solution**: The parallel compilation already handles this correctly - each script checks cache individually. The early return is just an optimization. However, we should make it more robust:

```csharp
public void PreloadAllScripts()
{
    var scriptDefinitionIds = _registry.GetByType("Script").ToList();
    var totalScripts = scriptDefinitionIds.Count;
    
    if (totalScripts == 0)
    {
        _logger.Information("No scripts to preload");
        return;
    }
    
    // Quick check: if cache has all scripts, skip (optimization)
    // Note: This is a best-effort check - parallel compilation will handle race conditions
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
    
    // ... rest of parallel compilation (handles race conditions correctly) ...
}
```

---

### 7. ⚠️ **ScriptChangeTracker Thread Safety**

**Issue**: `ScriptChangeTracker` uses `volatile bool`, but `MarkDirty()` and `MarkClean()` are not atomic operations.

**Location**: Design document section 4

**Current Design**:
```csharp
public static class ScriptChangeTracker
{
    private static volatile bool _isDirty = true;
    
    public static void MarkDirty() => _isDirty = true;  // ✅ Atomic (bool assignment)
    public static bool IsDirty() => _isDirty;           // ✅ Atomic (bool read)
    public static void MarkClean() => _isDirty = false; // ✅ Atomic (bool assignment)
}
```

**Analysis**: 
- `bool` assignments are atomic in C#
- `volatile` ensures visibility across threads
- This is actually thread-safe for this use case

**Verdict**: ✅ **No issue** - The implementation is thread-safe. `volatile bool` with atomic operations is sufficient.

---

### 8. ⚠️ **Missing Error Handling in PreloadAllScripts Early Return**

**Issue**: If cache check fails (exception), the early return logic doesn't handle it.

**Location**: Design document section 6

**Solution**: Add try-catch around cache check:

```csharp
public void PreloadAllScripts()
{
    var scriptDefinitionIds = _registry.GetByType("Script").ToList();
    var totalScripts = scriptDefinitionIds.Count;
    
    if (totalScripts == 0)
    {
        _logger.Information("No scripts to preload");
        return;
    }
    
    // Quick check: if cache has all scripts, skip (optimization)
    try
    {
        var allCached = true;
        foreach (var scriptDefId in scriptDefinitionIds)
        {
            if (!_compilationCache.TypeCache.TryGetCompiledType(scriptDefId, out _))
            {
                allCached = false;
                break;
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
    
    // ... rest of parallel compilation ...
}
```

---

### 9. ⚠️ **EntityCreatedEvent May Not Fire for Existing Entities**

**Issue**: `ScriptLifecycleSystem` subscribes to `EntityCreatedEvent` to mark dirty, but entities created before the system is initialized won't trigger the event.

**Location**: Design document section 4, line 1092-1096

**Problem**: 
- If entities with scripts exist before `ScriptLifecycleSystem` is created
- `EntityCreatedEvent` won't fire for those entities
- System might miss initial scripts

**Solution**: Mark dirty on system initialization, or check for existing entities:

```csharp
public ScriptLifecycleSystem(...)
{
    // ... existing code ...
    
    // Subscribe to entity events
    _subscriptions.Add(EventBus.Subscribe<EntityCreatedEvent>(OnEntityCreated));
    _subscriptions.Add(EventBus.Subscribe<EntityDestroyedEvent>(OnEntityDestroyed));
    
    // Mark dirty initially to ensure we process existing entities on first update
    ScriptChangeTracker.MarkDirty();
}
```

**Alternative**: Check for existing entities in first `Update()` call:

```csharp
public override void Update(in float deltaTime)
{
    // On first update, always process (handles entities created before system initialization)
    if (_previousAttachments.Count == 0)
        ScriptChangeTracker.MarkDirty();
    
    // Only query if scripts have changed
    if (!ScriptChangeTracker.IsDirty() && _previousAttachments.Count > 0)
    {
        return; // Skip this frame - no changes
    }
    
    // ... rest of update logic ...
}
```

---

### 10. ⚠️ **Missing Null Check for CompilationCache in SystemManager**

**Issue**: Design shows getting cache from `Game.Services`, but doesn't handle the case where it might be null during early SystemManager creation if registration order is wrong.

**Location**: Design document section 5, line 1658-1665

**Current Design**:
```csharp
var compilationCache = game.Services.GetService<IScriptCompilationCache>();
if (compilationCache == null)
{
    throw new InvalidOperationException(...);
}
```

**Analysis**: ✅ **Good** - The design already has null check and throws exception. This is correct per .cursorrules (fail fast, no fallback).

**Verdict**: ✅ **No issue** - Already handled correctly.

---

## SOLID/DRY Issues

### 11. ⚠️ **DRY Violation: Cache Registration Code Duplication**

**Issue**: The design shows cache creation code in multiple places (MonoBallGame, GameInitializationHelper).

**Location**: Design document section 8 and Step 1.5

**Solution**: Extract to helper method:

```csharp
// In GameInitializationHelper or MonoBallGame
public static IScriptCompilationCache CreateAndRegisterCompilationCache(
    Game game,
    ILogger logger
)
{
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

---

## .cursorrules Compliance

### 12. ✅ **All Issues Previously Identified Are Fixed**

- ✅ XML documentation added
- ✅ Null checks added
- ✅ Exception documentation added
- ✅ No empty catch blocks
- ✅ Proper error handling

---

## Arch ECS / Event System Issues

### 13. ⚠️ **EntityCreatedEvent Subscription Timing**

**Issue**: `ScriptLifecycleSystem` subscribes to `EntityCreatedEvent`, but if entities are created before the system subscribes, those events are missed.

**Solution**: Already addressed in issue #9 - mark dirty on initialization.

---

### 14. ⚠️ **Component Change Detection Not Event-Based**

**Issue**: The design relies on `ScriptChangeTracker.MarkDirty()` being called manually, which is error-prone.

**Problem**: 
- Easy to forget to call `MarkDirty()`
- No automatic detection of component changes
- Requires discipline from all systems that modify scripts

**Better Solution**: If Arch ECS supports component change callbacks, use those. Otherwise, the manual approach is acceptable but needs documentation.

**Documentation Needed**: 
- List all systems that modify `ScriptAttachmentComponent`
- Add code comments reminding to call `MarkDirty()`
- Consider adding a helper method:

```csharp
// In ScriptAttachmentComponent or helper
public static void SetScriptAttachment(
    World world,
    Entity entity,
    string scriptId,
    ScriptAttachmentData data
)
{
    if (!world.Has<ScriptAttachmentComponent>(entity))
    {
        world.Add(entity, new ScriptAttachmentComponent());
    }
    
    ref var component = ref world.Get<ScriptAttachmentComponent>(entity);
    if (component.Scripts == null)
        component.Scripts = new Dictionary<string, ScriptAttachmentData>();
    
    component.Scripts[scriptId] = data;
    ScriptChangeTracker.MarkDirty(); // Automatic dirty marking
}
```

---

## Summary of Required Fixes

### High Priority

1. ✅ **Fix cache registration location** - Move from `Initialize()` to `LoadContent()`
2. ✅ **Document all MarkDirty() call sites** - List all systems that modify scripts
3. ✅ **Handle existing entities on first update** - Mark dirty on system initialization

### Medium Priority

4. ✅ **Optimize PreloadAllScripts early return** - Remove redundant iteration
5. ✅ **Add error handling to cache check** - Wrap in try-catch
6. ✅ **Extract cache creation to helper** - DRY principle

### Low Priority / Acceptable

7. ✅ **ScriptChangeTracker static class** - Acceptable for simple flag (like EventBus)
8. ✅ **Manual MarkDirty() calls** - Acceptable, but needs documentation

---

## Recommended Updates to Design

1. **Section 8**: Change `Initialize()` to `LoadContent()` for cache registration
2. **Section 4**: Add note about marking dirty on system initialization
3. **Section 6**: Optimize early return check (remove redundant iteration)
4. **Step 1.5**: Update to reflect `LoadContent()` location
5. **Add new section**: Document all systems that must call `MarkDirty()`
