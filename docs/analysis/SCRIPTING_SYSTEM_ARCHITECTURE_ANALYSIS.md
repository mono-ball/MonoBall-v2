# Scripting System Architecture Analysis

**Generated:** 2025-01-27  
**Scope:** Complete analysis of scripting system changes for architecture issues, SOLID/DRY principles, async/multithreading, and .cursorrules compliance

---

## Executive Summary

The scripting system implementation is **architecturally sound** overall, but contains several **critical issues** that need addressing:

**Critical Issues:**
- ⚠️ **Thread Safety**: `ScriptLoaderService` uses non-thread-safe `Dictionary` collections without synchronization
- ⚠️ **Fail-Fast Violations**: `ScriptCompilerService` returns `null` instead of throwing exceptions
- ⚠️ **Resource Management**: Plugin script instances stored but may not be properly disposed
- ⚠️ **DRY Violations**: Duplicate key extraction logic in `ScriptLoaderService`

**Architecture Strengths:**
- ✅ **SOLID Compliance**: Clear separation of concerns (Loader, Compiler, Lifecycle)
- ✅ **No Async Issues**: All operations are synchronous (correct for MonoGame)
- ✅ **Proper Dependency Injection**: All dependencies injected via constructor
- ✅ **Event Integration**: Proper use of Arch EventBus for decoupled communication

**Medium Priority Issues:**
- ⚠️ **Code Duplication**: Key parsing logic duplicated
- ⚠️ **Missing Validation**: Some null checks could be more explicit

---

## 1. Architecture Analysis

### 1.1 ✅ Service Separation (Excellent)

**Location:** `ScriptLoaderService.cs`, `ScriptCompilerService.cs`, `ScriptLifecycleSystem.cs`

**Strengths:**
- **Single Responsibility**: Each service/system has a clear, focused purpose
  - `ScriptLoaderService`: File I/O and script loading
  - `ScriptCompilerService`: Roslyn compilation
  - `ScriptLifecycleSystem`: ECS integration and lifecycle management
- **Dependency Injection**: All dependencies injected via constructor with null checks
- **No God Classes**: Responsibilities are well-distributed

**Compliance:** ✅ SOLID (Single Responsibility Principle)

---

### 1.2 ⚠️ Thread Safety Issues (CRITICAL)

**Location:** `ScriptLoaderService.cs` lines 22-24

**Problem:**
```csharp
private readonly Dictionary<string, Type> _compiledScriptTypes = new();
private readonly Dictionary<string, Type> _pluginScriptTypes = new();
private readonly Dictionary<string, List<ScriptBase>> _pluginScriptsByMod = new();
```

**Issue:**
- `Dictionary<TKey, TValue>` is **not thread-safe**
- `List<T>` is **not thread-safe**
- These collections are accessed from multiple contexts:
  - `PreloadAllScripts()` - called during mod loading (main thread)
  - `CreateScriptInstance()` - called from `ScriptLifecycleSystem.Update()` (main thread, but could be called concurrently)
  - `InitializePluginScripts()` - called during initialization (main thread)
  - `UnloadModScripts()` - could be called during mod unloading (main thread)

**Impact:**
- **Race conditions**: Concurrent access could corrupt dictionary state
- **Data loss**: Entries could be lost during concurrent modifications
- **Exceptions**: `InvalidOperationException` ("Collection was modified") possible

**Current Context:**
- MonoGame runs on single thread (Update/Draw on main thread)
- However, if mods are loaded/unloaded dynamically, concurrent access is possible
- Future-proofing: If async loading is added, thread safety becomes critical

**Recommendation:**
```csharp
// Option 1: Use ConcurrentDictionary (if concurrent access is expected)
private readonly ConcurrentDictionary<string, Type> _compiledScriptTypes = new();
private readonly ConcurrentDictionary<string, Type> _pluginScriptTypes = new();
private readonly ConcurrentDictionary<string, List<ScriptBase>> _pluginScriptsByMod = new();

// Option 2: Use locks (if single-threaded access is guaranteed)
private readonly object _lock = new();
private readonly Dictionary<string, Type> _compiledScriptTypes = new();
```

**Priority:** HIGH (future-proofing, prevents hard-to-debug race conditions)

---

### 1.3 ⚠️ Fail-Fast Violations (CRITICAL)

**Location:** `ScriptCompilerService.cs` lines 50, 88

**Problem:**
```csharp
public Type? CompileScript(string scriptPath, ...)
{
    // ...
    return null; // ❌ Returns null instead of throwing
}

public Type? CompileScriptContent(string scriptContent, ...)
{
    // ...
    return null; // ❌ Returns null instead of throwing
}
```

**Issue:**
- Methods return `null` on failure instead of throwing exceptions
- Violates `.cursorrules` "NO FALLBACK CODE" principle
- Callers must check for null, leading to scattered null checks

**Current Usage:**
```csharp
// ScriptLoaderService.cs line 299
var compiledType = _compiler.CompileScriptContent(...);
if (compiledType != null) // ❌ Null check required
{
    _compiledScriptTypes[scriptDef.Id] = compiledType;
}
else
{
    _logger.Warning(...); // ❌ Silent failure
}
```

**Impact:**
- **Silent failures**: Scripts fail to compile but game continues
- **Inconsistent error handling**: Some failures throw, others return null
- **Debugging difficulty**: Hard to trace why scripts aren't loading

**Recommendation:**
```csharp
public Type CompileScript(string scriptPath, ...)
{
    if (string.IsNullOrWhiteSpace(scriptPath))
    {
        throw new ArgumentException("Script path cannot be null or empty", nameof(scriptPath));
    }

    if (!File.Exists(scriptPath))
    {
        throw new FileNotFoundException($"Script file not found: {scriptPath}", scriptPath);
    }

    try
    {
        string scriptContent = File.ReadAllText(scriptPath);
        return CompileScriptContent(scriptContent, scriptPath, additionalReferences);
    }
    catch (Exception ex) when (!(ex is ArgumentException || ex is FileNotFoundException))
    {
        throw new InvalidOperationException($"Failed to compile script: {scriptPath}", ex);
    }
}

public Type CompileScriptContent(string scriptContent, ...)
{
    if (string.IsNullOrWhiteSpace(scriptContent))
    {
        throw new ArgumentException("Script content cannot be null or empty", nameof(scriptContent));
    }

    // ... compilation logic ...

    if (!emitResult.Success)
    {
        var errors = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.GetMessage())
            .ToList();

        throw new InvalidOperationException(
            $"Script compilation failed for {scriptPath} with {errors.Count} errors: " +
            string.Join("; ", errors)
        );
    }

    // ... rest of compilation ...
}
```

**Priority:** HIGH (violates .cursorrules, causes silent failures)

---

### 1.4 ⚠️ Resource Management (MEDIUM)

**Location:** `ScriptLoaderService.cs` lines 24, 226-242, 518-552

**Issue:**
- Plugin script instances stored in `_pluginScriptsByMod` dictionary
- `ScriptBase` instances may hold resources (event subscriptions, etc.)
- `Dispose()` method disposes plugin scripts, but:
  - Entity-attached scripts (`_scriptInstances` in `ScriptLifecycleSystem`) are not tracked here
  - If `ScriptLoaderService` is disposed before scripts are cleaned up, resources leak

**Current Implementation:**
```csharp
protected virtual void Dispose(bool disposing)
{
    if (disposing)
    {
        // Dispose plugin scripts
        foreach (var modScripts in _pluginScriptsByMod.Values)
        {
            foreach (var script in modScripts)
            {
                try
                {
                    script.OnUnload();
                    script.Dispose(); // ✅ Good: disposing scripts
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error disposing plugin script during shutdown");
                }
            }
        }
        _pluginScriptsByMod.Clear();
        _compiledScriptTypes.Clear();
        _pluginScriptTypes.Clear();
    }
}
```

**Analysis:**
- ✅ Plugin scripts are properly disposed
- ⚠️ Entity-attached scripts are managed by `ScriptLifecycleSystem`, not `ScriptLoaderService` (correct separation)
- ✅ `ScriptLifecycleSystem` also implements `IDisposable` and cleans up scripts

**Recommendation:**
- Current implementation is correct
- Ensure `ScriptLifecycleSystem` is disposed before `ScriptLoaderService` (or handle gracefully)

**Priority:** LOW (current implementation is correct)

---

## 2. SOLID Principles Analysis

### 2.1 ✅ Single Responsibility Principle (SRP)

**Compliance:** ✅ Excellent

- `ScriptLoaderService`: File I/O and script loading only
- `ScriptCompilerService`: Compilation only
- `ScriptLifecycleSystem`: ECS integration only

**No violations found.**

---

### 2.2 ✅ Open/Closed Principle (OCP)

**Compliance:** ✅ Good

- Services use interfaces (`IScriptApiProvider`, `IResourceManager`)
- Scripts inherit from `ScriptBase` (open for extension)
- Compiler accepts additional references (extensible)

**No violations found.**

---

### 2.3 ✅ Liskov Substitution Principle (LSP)

**Compliance:** ✅ Good

- `ScriptBase` is properly abstracted
- All script instances can be substituted

**No violations found.**

---

### 2.4 ✅ Interface Segregation Principle (ISP)

**Compliance:** ✅ Good

- Interfaces are focused (`IScriptApiProvider`, `IResourceManager`)
- No fat interfaces

**No violations found.**

---

### 2.5 ✅ Dependency Inversion Principle (DIP)

**Compliance:** ✅ Excellent

- All dependencies injected via constructor
- Depend on abstractions (`IScriptApiProvider`, `IResourceManager`, `ILogger`)
- No concrete dependencies

**No violations found.**

---

## 3. DRY (Don't Repeat Yourself) Analysis

### 3.1 ⚠️ Key Parsing Logic Duplication (MEDIUM)

**Location:** `ScriptLoaderService.cs` lines 500-513

**Problem:**
```csharp
private string ExtractModIdFromKey(string key)
{
    var colonIndex = key.IndexOf(':');
    return colonIndex > 0 ? key.Substring(0, colonIndex) : key;
}

private string ExtractScriptPathFromKey(string key)
{
    var colonIndex = key.IndexOf(':');
    return colonIndex > 0 ? key.Substring(colonIndex + 1) : key;
}
```

**Issue:**
- Both methods parse the same key format (`modId:scriptPath`)
- Logic is duplicated (finding colon index)
- If key format changes, both methods must be updated

**Recommendation:**
```csharp
private (string modId, string scriptPath) ParsePluginScriptKey(string key)
{
    var colonIndex = key.IndexOf(':');
    if (colonIndex > 0)
    {
        return (key.Substring(0, colonIndex), key.Substring(colonIndex + 1));
    }
    return (key, key); // Fallback: treat entire key as modId
}

private string ExtractModIdFromKey(string key)
{
    return ParsePluginScriptKey(key).modId;
}

private string ExtractScriptPathFromKey(string key)
{
    return ParsePluginScriptKey(key).scriptPath;
}
```

**Priority:** MEDIUM (code quality, maintainability)

---

### 3.2 ✅ No Other DRY Violations

**Analysis:**
- Script loading logic is not duplicated
- Compilation logic is centralized
- Lifecycle management is in one place

**No other violations found.**

---

## 4. Async/Multithreading Analysis

### 4.1 ✅ No Async/Await Issues (EXCELLENT)

**Analysis:**
- ✅ All methods are synchronous
- ✅ No `async/await` patterns found
- ✅ No `Task` or `Task<T>` return types
- ✅ Correct for MonoGame (single-threaded game loop)

**Compliance:** ✅ Perfect

**Rationale:**
- MonoGame runs on single thread
- Scripts are pre-loaded during mod loading phase (before game loop)
- No need for async operations
- Synchronous compilation is acceptable (happens during initialization)

---

### 4.2 ⚠️ Thread Safety Concerns (HIGH)

**Issue:** See Section 1.2 (Thread Safety Issues)

**Summary:**
- Non-thread-safe collections used without synchronization
- Currently safe (single-threaded MonoGame)
- Future risk if async loading is added

**Recommendation:**
- Add thread safety now (future-proofing)
- Use `ConcurrentDictionary` or locks

---

## 5. Error Handling Analysis

### 5.1 ⚠️ Fail-Fast Violations (CRITICAL)

**Issue:** See Section 1.3 (Fail-Fast Violations)

**Summary:**
- `ScriptCompilerService` returns `null` instead of throwing
- Violates `.cursorrules` "NO FALLBACK CODE" principle
- Causes silent failures

**Recommendation:**
- Change return types from `Type?` to `Type`
- Throw exceptions on failure
- Update callers to handle exceptions

---

### 5.2 ✅ Proper Exception Handling (GOOD)

**Location:** `ScriptLoaderService.cs`, `ScriptLifecycleSystem.cs`

**Strengths:**
- Try-catch blocks around critical operations
- Exceptions logged with context
- Error events fired (`ScriptErrorEvent`)

**Example:**
```csharp
try
{
    scriptInstance.Initialize(context);
    scriptInstance.RegisterEventHandlers(context);
}
catch (Exception ex)
{
    _logger.Error(ex, "Error initializing script...");
    var errorEvent = new ScriptErrorEvent { ... };
    EventBus.Send(ref errorEvent);
}
```

**Compliance:** ✅ Good

---

## 6. Resource Management Analysis

### 6.1 ✅ Proper Disposal Pattern (GOOD)

**Location:** `ScriptLoaderService.cs` lines 518-552, `ScriptLifecycleSystem.cs` lines 388-422

**Strengths:**
- Both implement `IDisposable`
- Standard dispose pattern used
- Resources properly cleaned up

**Compliance:** ✅ Good

---

### 6.2 ⚠️ Event Subscription Cleanup (MEDIUM)

**Location:** `ScriptLifecycleSystem.cs` line 68

**Issue:**
- `EventBus.Subscribe<EntityDestroyedEvent>(OnEntityDestroyed)` called in constructor
- Unsubscribed in `Dispose()` ✅ Good
- However, if system is not disposed, event subscription leaks

**Current Implementation:**
```csharp
public ScriptLifecycleSystem(...)
{
    // ...
    EventBus.Subscribe<EntityDestroyedEvent>(OnEntityDestroyed);
}

protected virtual void Dispose(bool disposing)
{
    if (!_disposed && disposing)
    {
        EventBus.Unsubscribe<EntityDestroyedEvent>(OnEntityDestroyed); // ✅ Good
        // ...
    }
    _disposed = true;
}
```

**Analysis:**
- ✅ Properly unsubscribes in `Dispose()`
- ⚠️ Relies on proper disposal (should be guaranteed by ECS framework)

**Recommendation:**
- Current implementation is correct
- Ensure ECS framework disposes systems properly

**Priority:** LOW (current implementation is correct)

---

## 7. Integration with Arch ECS

### 7.1 ✅ Proper ECS Integration (EXCELLENT)

**Location:** `ScriptLifecycleSystem.cs`

**Strengths:**
- Inherits from `BaseSystem<World, float>` ✅
- Caches `QueryDescription` in constructor ✅
- Uses `World.Query()` correctly ✅
- Fires events via `EventBus.Send()` ✅
- Subscribes to events ✅
- Implements `IDisposable` for cleanup ✅

**Compliance:** ✅ Perfect

---

### 7.2 ✅ Component Modification Pattern (GOOD)

**Location:** `ScriptLifecycleSystem.cs` lines 243-256

**Implementation:**
```csharp
if (World.IsAlive(entity) && World.Has<ScriptAttachmentComponent>(entity))
{
    ref var component = ref World.Get<ScriptAttachmentComponent>(entity);
    if (component.Scripts != null && component.Scripts.ContainsKey(...))
    {
        var updatedAttachment = component.Scripts[...];
        updatedAttachment.IsInitialized = true;
        component.Scripts[...] = updatedAttachment;
        // ✅ Correct: ref parameter modifications persist automatically
    }
}
```

**Analysis:**
- ✅ Correctly uses `ref` parameter
- ✅ Modifications persist automatically (no `World.Set()` needed)
- ✅ Checks entity is alive before modifying

**Compliance:** ✅ Perfect

---

## 8. Recommendations Summary

### Critical (Must Fix)

1. **Thread Safety** (Section 1.2)
   - Use `ConcurrentDictionary` or add locks
   - Future-proofing for potential async loading

2. **Fail-Fast Violations** (Section 1.3)
   - Change `ScriptCompilerService` methods to throw exceptions
   - Update return types from `Type?` to `Type`
   - Update callers to handle exceptions

### Medium Priority

3. **DRY Violations** (Section 3.1)
   - Extract key parsing logic to single method
   - Reduces code duplication

### Low Priority

4. **Event Subscription Cleanup** (Section 6.2)
   - Current implementation is correct
   - Ensure ECS framework disposes systems properly

---

## 9. Testing Considerations

### 9.1 Thread Safety Testing

**Recommendation:** Add unit tests for concurrent access:
- Multiple threads calling `CreateScriptInstance()` simultaneously
- Mod loading/unloading during script access
- Cache modifications during concurrent reads

### 9.2 Fail-Fast Testing

**Recommendation:** Add tests verifying exceptions are thrown:
- Null/empty script paths → `ArgumentException`
- Missing script files → `FileNotFoundException`
- Compilation failures → `InvalidOperationException` with error details

### 9.3 Lifecycle Testing

**Recommendation:** Add tests for:
- Script initialization on entity attachment
- Script cleanup on entity destruction
- Plugin script initialization/unloading
- Proper disposal of all resources

---

## Conclusion

The scripting system implementation is **architecturally sound** with **excellent SOLID compliance** and **proper ECS integration**. However, there are **two critical issues** that must be addressed:

1. **Thread Safety**: Non-thread-safe collections need synchronization
2. **Fail-Fast Violations**: Methods return `null` instead of throwing exceptions

The implementation correctly avoids async/await (appropriate for MonoGame) and properly integrates with Arch ECS. The main issues are compliance-related (fail-fast) and future-proofing (thread safety).

**Priority Order:**
1. Fix fail-fast violations (compliance with .cursorrules)
2. Add thread safety (future-proofing)
3. Extract duplicated key parsing logic (code quality)

