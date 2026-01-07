# Scripting System Architecture Analysis

**Status**: Design Review  
**Date**: 2025-01-XX  
**Purpose**: Identify architecture issues, Arch ECS violations, event system problems, and future considerations

---

## 🔴 Critical Architecture Issues

### 1. **Component Design Violations**

#### Issue: Reference Types in Struct Components

**Problem**: `ScriptAttachmentComponent` and `ScriptStateComponent` contain reference types, violating ECS principles.

**Current Design**:
```csharp
public struct ScriptAttachmentComponent
{
    public string ScriptDefinitionId { get; set; }
    public object? ScriptInstance { get; set; }  // ❌ Reference type in struct
    public Dictionary<string, object>? ParameterOverrides { get; set; }  // ❌ Reference type
}

public struct ScriptStateComponent
{
    public Dictionary<string, object> State { get; set; }  // ❌ Reference type
}
```

**Why This Is A Problem**:
- Components should be pure value types (structs)
- Reference types in components break ECS principles
- Can cause memory leaks and serialization issues
- Makes components non-copyable and non-comparable

**Solution**:
```csharp
// Option 1: Store script instance outside component (in system/service)
public struct ScriptAttachmentComponent
{
    public string ScriptDefinitionId { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public string ModId { get; set; }
    internal bool IsInitialized { get; set; }
    // ScriptInstance stored in ScriptLifecycleSystem dictionary
    // ParameterOverrides stored separately or in ScriptStateComponent
}

// Option 2: Use component IDs/references instead of instances
public struct ScriptAttachmentComponent
{
    public string ScriptDefinitionId { get; set; }
    public int ScriptInstanceId { get; set; }  // Reference to instance in service
    // ...
}
```

**Recommended**: Store script instances in `ScriptLifecycleSystem` dictionary keyed by `(Entity, ScriptDefinitionId)`.

---

### 2. **System Design Issues**

#### Issue: ScriptLoaderSystem Doesn't Query Entities

**Problem**: `ScriptLoaderSystem` doesn't operate on entities - it operates on file system and mod registry. This violates the "Systems operate on entities" principle.

**Current Design**:
- `ScriptLoaderSystem` queries `DefinitionRegistry` (not entities)
- Loads and compiles scripts
- Initializes plugin scripts

**Why This Is A Problem**:
- Systems should query entities, not external registries
- Mixing concerns (file I/O, compilation, initialization)
- Not following ECS patterns

**Solution**:
- **Split into Service + System**:
  - `ScriptLoaderService` - Handles file I/O, compilation, caching (not a system)
  - `ScriptLifecycleSystem` - Queries entities with `ScriptAttachmentComponent`, uses service to load scripts

**Architecture**:
```
ScriptLoaderService (Service, not System)
├── LoadScriptFromDefinition(ScriptDefinition) → ScriptInstance
├── LoadPluginScript(string path) → ScriptInstance
└── Cache management

ScriptLifecycleSystem (System)
├── Queries entities with ScriptAttachmentComponent
├── Uses ScriptLoaderService to load scripts
└── Manages script initialization/cleanup
```

---

#### Issue: Component Removal Detection

**Problem**: `ScriptLifecycleSystem` needs to detect when `ScriptAttachmentComponent` is removed, but Arch ECS doesn't provide component removal events.

**Current Design**:
- System queries entities with component
- No way to detect when component is removed

**Why This Is A Problem**:
- Can't call `OnUnload()` when component is removed
- Script subscriptions leak
- Script instances not cleaned up

**Solution**:
- **Track Previous State**: Store set of `(Entity, ScriptDefinitionId)` pairs from previous frame
- **Compare**: In Update(), compare current state with previous state
- **Cleanup**: For entities that had component but no longer do, call `OnUnload()`

**Implementation**:
```csharp
private HashSet<(Entity, string)> _previousAttachments = new();

public override void Update(in float deltaTime)
{
    var currentAttachments = new HashSet<(Entity, string)>();
    
    World.Query(in _queryDescription, (Entity entity, ref ScriptAttachmentComponent attachment) =>
    {
        currentAttachments.Add((entity, attachment.ScriptDefinitionId));
        // ... initialization logic
    });
    
    // Find removed attachments
    foreach (var (entity, scriptId) in _previousAttachments)
    {
        if (!currentAttachments.Contains((entity, scriptId)))
        {
            // Component removed - cleanup
            CleanupScript(entity, scriptId);
        }
    }
    
    _previousAttachments = currentAttachments;
}
```

**Alternative**: Use entity destruction events to detect when entities are destroyed.

---

#### Issue: Async Operations in Systems

**Problem**: `ScriptLoaderSystem` uses `async/await` but systems are synchronous.

**Current Design**:
```csharp
var scriptInstance = await _scriptCompilerService.LoadScriptAsync(scriptFilePath);
```

**Why This Is A Problem**:
- Systems run synchronously in `Update()` method
- Can't use `await` in synchronous context
- Blocks game loop if script compilation is slow

**Solution**:
- **Pre-load Scripts**: Load all scripts during mod loading phase (before game loop starts)
- **Synchronous Compilation**: Use synchronous compilation API or `GetAwaiter().GetResult()` (with caution)
- **Background Loading**: Load scripts in background thread, mark as "loading" in component

**Recommended**: Pre-load all scripts during mod loading phase. Scripts should be compiled and cached before entities are created.

---

### 3. **Event System Issues**

#### Issue: Event Subscription Cleanup

**Problem**: `ScriptBase` needs to track event subscriptions for cleanup, but `EventBus.Subscribe()` doesn't return `IDisposable`.

**Current Design**:
```csharp
protected void On<TEvent>(Action<TEvent> handler)
{
    EventBus.Subscribe(handler);  // No return value for cleanup
    // How do we track this for cleanup?
}
```

**Why This Is A Problem**:
- Can't unsubscribe without storing handler reference
- Memory leaks if scripts don't clean up properly
- Need to track all subscriptions

**Solution**:
- **Subscription Wrapper**: Create wrapper that tracks subscriptions and implements `IDisposable`
- **Store Handlers**: Store handler references in `ScriptBase` for cleanup

**Implementation**:
```csharp
public abstract class ScriptBase
{
    private readonly List<IDisposable> _subscriptions = new();

    protected void On<TEvent>(Action<TEvent> handler)
        where TEvent : struct
    {
        var subscription = new EventSubscription<TEvent>(handler);
        _subscriptions.Add(subscription);
    }

    public virtual void OnUnload()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
        _subscriptions.Clear();
    }
}

internal class EventSubscription<T> : IDisposable
    where T : struct
{
    private readonly Action<T> _handler;
    private bool _disposed;

    public EventSubscription(Action<T> handler)
    {
        _handler = handler;
        EventBus.Subscribe(_handler);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            EventBus.Unsubscribe(_handler);
            _disposed = true;
        }
    }
}
```

---

#### Issue: Plugin Script Cleanup

**Problem**: Plugin scripts are initialized immediately but there's no cleanup mechanism when mods unload.

**Current Design**:
- Plugin scripts initialized when mod loads
- No cleanup when mod unloads

**Why This Is A Problem**:
- Event subscriptions leak
- Script instances not disposed
- Memory leaks

**Solution**:
- **Track Plugin Scripts**: Store plugin script instances in `ScriptLoaderService`
- **Mod Unload Event**: Subscribe to mod unload events (if they exist)
- **Cleanup on Unload**: Call `OnUnload()` on all plugin scripts from unloaded mod

**Implementation**:
```csharp
public class ScriptLoaderService
{
    private readonly Dictionary<string, List<ScriptBase>> _pluginScriptsByMod = new();

    public void LoadPluginScript(string modId, string scriptPath)
    {
        var instance = LoadScript(scriptPath);
        if (instance is ScriptBase script)
        {
            if (!_pluginScriptsByMod.ContainsKey(modId))
            {
                _pluginScriptsByMod[modId] = new List<ScriptBase>();
            }
            _pluginScriptsByMod[modId].Add(script);
        }
    }

    public void UnloadModScripts(string modId)
    {
        if (_pluginScriptsByMod.TryGetValue(modId, out var scripts))
        {
            foreach (var script in scripts)
            {
                script.OnUnload();
            }
            _pluginScriptsByMod.Remove(modId);
        }
    }
}
```

---

### 4. **Architecture Violations**

#### Issue: ScriptContext Exposes World Directly

**Problem**: `ScriptContext` exposes `World` directly, giving scripts full access to ECS world.

**Current Design**:
```csharp
public class ScriptContext
{
    public World World { get; }  // ❌ Too permissive
    public Entity? Entity { get; }
    // ...
}
```

**Why This Is A Problem**:
- Scripts can directly manipulate world (bypassing systems)
- No validation or safety checks
- Breaks encapsulation
- Makes sandboxing impossible

**Solution**:
- **Remove Direct World Access**: Don't expose `World` directly
- **Provide Safe Operations**: Expose only safe operations via `ScriptContext` methods
- **Entity Operations**: Provide `CreateEntity()`, `DestroyEntity()`, `Query()` methods that wrap World operations

**Implementation**:
```csharp
public class ScriptContext
{
    private readonly World _world;  // Private, not exposed

    // Safe entity operations
    public Entity CreateEntity(params object[] components)
    {
        return _world.Create(components);
    }

    public void DestroyEntity(Entity entity)
    {
        if (!_world.IsAlive(entity))
        {
            throw new InvalidOperationException("Entity is not alive");
        }
        _world.Destroy(entity);
    }

    public void Query<T1>(Action<Entity, ref T1> action)
        where T1 : struct
    {
        _world.Query(new QueryDescription().WithAll<T1>(), action);
    }
}
```

---

#### Issue: Script Instance Storage

**Problem**: Can't store script instances in components (reference types), but need to track them.

**Current Design**:
- `ScriptAttachmentComponent.ScriptInstance` - violates ECS

**Solution**:
- **Store in System/Service**: Store script instances in `ScriptLifecycleSystem` dictionary
- **Key by Entity + ScriptId**: Use `(Entity, string ScriptDefinitionId)` as key

**Implementation**:
```csharp
public class ScriptLifecycleSystem : BaseSystem<World, float>, IDisposable
{
    // Store script instances outside components
    private readonly Dictionary<(Entity, string), ScriptBase> _scriptInstances = new();
    
    // Store initialization state
    private readonly HashSet<(Entity, string)> _initializedScripts = new();
}
```

---

### 5. **State Management Issues**

#### Issue: ScriptStateComponent Dictionary

**Problem**: `Dictionary<string, object>` in component is a reference type.

**Current Design**:
```csharp
public struct ScriptStateComponent
{
    public Dictionary<string, object> State { get; set; }  // ❌ Reference type
}
```

**Why This Is A Problem**:
- Reference type in struct component
- All entities share same dictionary reference (if not initialized)
- Serialization issues

**Solution**:
- **Option 1**: Store state in system dictionary (like script instances)
- **Option 2**: Use component-based state (one component per state value)
- **Option 3**: Use `EntityVariablesComponent` (if it exists)

**Recommended**: Use `EntityVariablesComponent` for script state, or store in system dictionary.

---

## ⚠️ Arch ECS Best Practice Violations

### 1. **QueryDescription Not Cached**

**Problem**: `ScriptLifecycleSystem` creates `QueryDescription` in Update().

**Current Design**:
```csharp
public override void Update(in float deltaTime)
{
    World.Query(new QueryDescription().WithAll<ScriptAttachmentComponent>(), ...);
    // ❌ Creating QueryDescription every frame
}
```

**Solution**:
```csharp
private readonly QueryDescription _queryDescription;

public ScriptLifecycleSystem(World world, ...) : base(world)
{
    _queryDescription = new QueryDescription()
        .WithAll<ScriptAttachmentComponent>();
}
```

---

### 2. **System Doesn't Implement IDisposable**

**Problem**: `ScriptLifecycleSystem` subscribes to events but doesn't implement `IDisposable`.

**Current Design**:
- System subscribes to events
- No cleanup mechanism

**Solution**:
```csharp
public class ScriptLifecycleSystem : BaseSystem<World, float>, IDisposable
{
    private bool _disposed = false;

    public ScriptLifecycleSystem(World world, ...) : base(world)
    {
        EventBus.Subscribe<EntityDestroyedEvent>(OnEntityDestroyed);
    }

    public new void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            EventBus.Unsubscribe<EntityDestroyedEvent>(OnEntityDestroyed);
            // Cleanup script instances
            foreach (var script in _scriptInstances.Values)
            {
                script.OnUnload();
            }
            _scriptInstances.Clear();
        }
        _disposed = true;
    }
}
```

---

### 3. **System Priority Not Defined**

**Problem**: `ScriptLifecycleSystem` doesn't specify execution priority.

**Solution**:
```csharp
public class ScriptLifecycleSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    public int Priority => SystemPriority.ScriptLifecycle;
    // ...
}
```

Add to `SystemPriority`:
```csharp
public static class SystemPriority
{
    // ... existing priorities
    public const int ScriptLifecycle = 25;  // After map loading, before player
}
```

---

## 🎯 Event System Issues

### 1. **EventBus Doesn't Support Priority**

**Problem**: Current `EventBus` doesn't support handler priority, but design mentions it.

**Current Implementation**:
- `EventBus.Subscribe<T>()` - no priority parameter
- Handlers execute in registration order

**Solution**:
- **Option 1**: Add priority support to `EventBus`
- **Option 2**: Document that priority is not supported yet
- **Option 3**: Use separate event buses for different priority levels

**Recommended**: Add priority support to `EventBus` or document limitation.

---

### 2. **Ref Event Handler Cleanup**

**Problem**: `EventBus.Unsubscribe<T>(RefAction<T>)` requires exact handler reference match.

**Current Design**:
```csharp
On<MovementStartedEvent>(ref evt => { ... });
// How do we unsubscribe this lambda?
```

**Why This Is A Problem**:
- Lambdas create new delegate instances
- Can't match handler for unsubscribe
- Memory leaks

**Solution**:
- **Store Handler References**: Store handler in `EventSubscription` wrapper
- **Use Named Methods**: Prefer named methods over lambdas for event handlers

**Implementation**:
```csharp
// ❌ BAD - Can't unsubscribe
On<MovementStartedEvent>(ref evt => { ... });

// ✅ GOOD - Can unsubscribe
On<MovementStartedEvent>(OnMovementStarted);
private void OnMovementStarted(ref MovementStartedEvent evt) { ... }
```

---

### 3. **Event Subscription Tracking**

**Problem**: Need to track all subscriptions for cleanup, but `EventBus` doesn't provide subscription tracking.

**Solution**:
- **Subscription Wrapper**: Wrap all subscriptions in `IDisposable` wrapper
- **Track in ScriptBase**: Store subscriptions in list for cleanup

---

## 🔄 Lifecycle Management Issues

### 1. **Plugin Script Lifecycle**

**Problem**: Plugin scripts initialized immediately but no cleanup on mod unload.

**Current Design**:
- Plugin scripts initialized when mod loads
- No cleanup mechanism

**Solution**:
- **Track by Mod ID**: Store plugin scripts by mod ID
- **Unload on Mod Unload**: Cleanup when mod unloads (if mod unloading is supported)

---

### 2. **Hot-Reload State Preservation**

**Problem**: Hot-reload needs to preserve state, but current design doesn't specify how.

**Current Design**:
- State stored in `ScriptStateComponent`
- But component has reference type (Dictionary)

**Solution**:
- **Pre-Unload State Extraction**: Before reload, extract state from component
- **Post-Load State Restoration**: After reload, restore state to component
- **Use EntityVariablesComponent**: Store state in existing variable component

---

### 3. **Entity Destruction Cleanup**

**Problem**: When entity is destroyed, scripts attached to it need cleanup.

**Current Design**:
- System queries entities with component
- But destroyed entities won't be in query

**Solution**:
- **Subscribe to Entity Events**: If entity destruction events exist, subscribe to them
- **Track Entity IDs**: Store entity IDs and check if entities are alive
- **Periodic Cleanup**: Periodically check if tracked entities are still alive

**Implementation**:
```csharp
private void OnEntityDestroyed(ref EntityDestroyedEvent evt)
{
    // Find all scripts attached to this entity
    var scriptsToCleanup = _scriptInstances
        .Where(kvp => kvp.Key.Item1 == evt.Entity)
        .ToList();
    
    foreach (var ((entity, scriptId), script) in scriptsToCleanup)
    {
        script.OnUnload();
        _scriptInstances.Remove((entity, scriptId));
    }
}
```

---

## 🚀 Performance Considerations

### 1. **Script Instance Lookup**

**Problem**: Looking up script instances by `(Entity, ScriptDefinitionId)` every frame.

**Current Design**:
- Dictionary lookup per attachment per frame

**Impact**:
- O(1) lookup, but still overhead with many scripts

**Solution**:
- **Cache Lookups**: Cache script instance in component (but can't - reference type)
- **Use Component for Reference**: Store script instance ID in component, lookup in dictionary
- **Optimize Queries**: Only query entities that need script updates

---

### 2. **Event Handler Overhead**

**Problem**: Many scripts subscribing to same events increases handler count.

**Current Design**:
- Each script subscribes independently
- All handlers execute for every event

**Impact**:
- Linear performance degradation with script count

**Solution**:
- **Event Filtering**: Filter events at subscription level (entity/tile filtering)
- **Batch Processing**: Batch event processing for scripts
- **Priority-Based Early Exit**: Allow high-priority handlers to prevent lower-priority execution

---

### 3. **Script Compilation Overhead**

**Problem**: Script compilation is expensive, but design doesn't specify when it happens.

**Current Design**:
- Scripts compile on first use (lazy loading)

**Impact**:
- First frame stutter when script is first used
- Compilation blocks game loop

**Solution**:
- **Pre-compile**: Compile all scripts during mod loading phase
- **Background Compilation**: Compile in background thread (complex)
- **Cache Compiled Scripts**: Cache compiled scripts to disk

**Recommended**: Pre-compile all scripts during mod loading.

---

## 🔮 Future Considerations

### 1. **Script Dependencies**

**Problem**: Scripts might depend on other scripts or mods, but no dependency system.

**Consideration**:
- Script definition should specify dependencies
- Load order matters
- Circular dependency detection

---

### 2. **Script Error Isolation**

**Problem**: Script errors can crash entire game or affect other scripts.

**Consideration**:
- **Try-Catch Wrappers**: Wrap all script execution in try-catch
- **Error Events**: Fire `ScriptErrorEvent` instead of throwing
- **Script Disabling**: Automatically disable scripts that error repeatedly
- **Error Reporting**: Log errors with script context

---

### 3. **Script Debugging**

**Problem**: No debugging support for scripts.

**Consideration**:
- **Breakpoints**: Support breakpoints in scripts
- **Variable Inspection**: Inspect script variables at runtime
- **Call Stack**: Show script call stack in debugger
- **Performance Profiling**: Profile script execution time

---

### 4. **Script Validation**

**Problem**: No validation of scripts before loading.

**Consideration**:
- **Type Checking**: Validate script types match definition
- **Parameter Validation**: Validate parameters match definition
- **API Usage**: Check for unsafe API usage
- **Dependency Checking**: Verify script dependencies are available

---

### 5. **Script Versioning**

**Problem**: Script definitions might change, breaking existing scripts.

**Consideration**:
- **Version Field**: Add version to `ScriptDefinition`
- **Migration Support**: Support migrating old scripts to new versions
- **Backward Compatibility**: Document breaking changes

---

### 6. **Script Performance Profiling**

**Problem**: No way to measure script performance.

**Consideration**:
- **Execution Time Tracking**: Track time spent in each script
- **Event Handler Profiling**: Profile event handler execution
- **Memory Usage**: Track script memory usage
- **Performance Warnings**: Warn about slow scripts

---

### 7. **Script Sandboxing (Future)**

**Problem**: Current design has no sandboxing, but future might need it.

**Consideration**:
- **API Restrictions**: Restrict which APIs scripts can access
- **Resource Limits**: Limit memory/CPU usage per script
- **Code Analysis**: Analyze script code for unsafe operations
- **Permission System**: Require permissions for certain operations

---

## 📋 Summary of Required Changes

### Critical (Must Fix)

1. ✅ **Remove reference types from components**
   - Store script instances in system dictionary
   - Use `EntityVariablesComponent` for state

2. ✅ **Split ScriptLoaderSystem into Service + System**
   - `ScriptLoaderService` - File I/O, compilation
   - `ScriptLifecycleSystem` - Entity queries, initialization

3. ✅ **Implement IDisposable on systems**
   - `ScriptLifecycleSystem` must implement `IDisposable`
   - Cleanup event subscriptions and script instances

4. ✅ **Cache QueryDescription**
   - Store `QueryDescription` as instance field

5. ✅ **Track component removal**
   - Store previous frame state
   - Compare and cleanup removed components

6. ✅ **Remove direct World access from ScriptContext**
   - Provide safe wrapper methods only

7. ✅ **Event subscription cleanup**
   - Wrap subscriptions in `IDisposable`
   - Track subscriptions in `ScriptBase`

### Important (Should Fix)

8. ⚠️ **Pre-load scripts during mod loading**
   - Don't compile scripts during game loop

9. ⚠️ **Plugin script cleanup**
   - Track plugin scripts by mod ID
   - Cleanup on mod unload

10. ⚠️ **System priority**
    - Add `IPrioritizedSystem` to `ScriptLifecycleSystem`
    - Define priority in `SystemPriority`

### Future Enhancements

11. 🔮 **Event priority support**
12. 🔮 **Script error isolation**
13. 🔮 **Script debugging support**
14. 🔮 **Script validation**
15. 🔮 **Performance profiling**

---

## 🎯 Recommended Architecture Changes

### Component Design (Fixed)

```csharp
public struct ScriptAttachmentComponent
{
    public string ScriptDefinitionId { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public string ModId { get; set; }
    internal bool IsInitialized { get; set; }
    // No ScriptInstance - stored in system
    // No ParameterOverrides - stored separately or in EntityVariablesComponent
}
```

### System Design (Fixed)

```csharp
public class ScriptLifecycleSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly QueryDescription _queryDescription;
    private readonly ScriptLoaderService _scriptLoader;
    private readonly Dictionary<(Entity, string), ScriptBase> _scriptInstances = new();
    private readonly HashSet<(Entity, string)> _initializedScripts = new();
    private HashSet<(Entity, string)> _previousAttachments = new();
    private bool _disposed = false;

    public int Priority => SystemPriority.ScriptLifecycle;

    public ScriptLifecycleSystem(World world, ScriptLoaderService scriptLoader, ...) : base(world)
    {
        _scriptLoader = scriptLoader ?? throw new ArgumentNullException(nameof(scriptLoader));
        _queryDescription = new QueryDescription().WithAll<ScriptAttachmentComponent>();
    }

    public override void Update(in float deltaTime)
    {
        var currentAttachments = new HashSet<(Entity, string)>();
        
        World.Query(in _queryDescription, (Entity entity, ref ScriptAttachmentComponent attachment) =>
        {
            if (!attachment.IsActive) return;
            
            var key = (entity, attachment.ScriptDefinitionId);
            currentAttachments.Add(key);
            
            // Initialize if needed
            if (!_initializedScripts.Contains(key))
            {
                InitializeScript(entity, ref attachment);
            }
        });
        
        // Cleanup removed attachments
        foreach (var key in _previousAttachments)
        {
            if (!currentAttachments.Contains(key))
            {
                CleanupScript(key.Item1, key.Item2);
            }
        }
        
        _previousAttachments = currentAttachments;
    }

    public new void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            // Cleanup all scripts
            foreach (var script in _scriptInstances.Values)
            {
                script.OnUnload();
            }
            _scriptInstances.Clear();
            _initializedScripts.Clear();
        }
        _disposed = true;
    }
}
```

### Service Design (New)

```csharp
public class ScriptLoaderService
{
    private readonly Dictionary<string, ScriptBase> _compiledScripts = new();
    private readonly Dictionary<string, List<ScriptBase>> _pluginScriptsByMod = new();
    private readonly ScriptCompilerService _compiler;

    public ScriptBase? LoadScriptFromDefinition(ScriptDefinition definition, string modDirectory)
    {
        // Load and compile script
        // Cache by definition ID
    }

    public void LoadPluginScript(string modId, string scriptPath)
    {
        // Load plugin script
        // Track by mod ID
    }

    public void UnloadModScripts(string modId)
    {
        // Cleanup plugin scripts for mod
    }
}
```

---

## ✅ Conclusion

The design has several critical architecture issues that must be addressed:

1. **Component purity** - Remove reference types from components
2. **System/service separation** - Split loading logic into service
3. **Lifecycle management** - Proper cleanup and disposal
4. **Event subscription tracking** - Track subscriptions for cleanup
5. **World access** - Remove direct World access from ScriptContext

Once these issues are addressed, the design will be aligned with Arch ECS best practices and the project's coding standards.


