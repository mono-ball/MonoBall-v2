# Scripting System Plan Analysis

**Status**: Plan Review  
**Date**: 2025-01-XX  
**Purpose**: Identify issues, gaps, and inconsistencies between the implementation plan and the design document

---

## Critical Issues

### 1. **Script Instance Cloning/Instantiation**

**Issue**: The plan shows `ScriptLoaderService.GetScriptInstance()` returning a single cached instance, but each entity needs its own script instance.

**Problem**:
- Scripts have instance state (fields, event subscriptions)
- Multiple entities with the same script definition cannot share the same instance
- Each entity needs its own initialized ScriptBase instance

**Design Reference**: The design shows `_scriptInstances` dictionary keyed by `(Entity, ScriptDefinitionId)`, implying one instance per entity.

**Solution**:
- `ScriptLoaderService` should cache compiled script types/factories, not instances
- `ScriptLifecycleSystem` should create new instances per entity from the cached type
- Or: `ScriptLoaderService` should provide a factory method that creates new instances

**Required Change**:
```csharp
// ScriptLoaderService should return a factory or type, not an instance
public ScriptBase CreateScriptInstance(string definitionId)
{
    // Get compiled script type/factory
    // Create new instance
    // Return new instance
}
```

---

### 2. **EntityDestroyedEvent Missing**

**Issue**: The plan says to create `EntityDestroyedEvent` "if needed", but the design requires it.

**Problem**:
- `ScriptLifecycleSystem` subscribes to `EntityDestroyedEvent` in constructor
- Event doesn't exist in codebase (grep found no matches)
- System will fail to compile without this event

**Solution**:
- **Must create** `EntityDestroyedEvent` in Phase 6 (Events)
- Event should contain: `Entity Entity` property
- Systems that destroy entities should fire this event

**Required Change**:
- Move `EntityDestroyedEvent` from "if needed" to required
- Add to Phase 6 todos as critical
- Document that systems destroying entities must fire this event

---

### 3. **Plugin Script Initialization Timing**

**Issue**: Plan says to initialize plugin scripts immediately in `PreloadAllScripts()`, but they need `IScriptApiProvider` which may not be ready.

**Problem**:
- Plugin scripts need `ScriptContext` with `IScriptApiProvider`
- `IScriptApiProvider` is created in Phase 4
- `PreloadAllScripts()` is called in Phase 3 (SystemManager initialization)
- Phase 4 (API Integration) happens after Phase 3

**Design Reference**: Design shows plugin scripts initialized during mod loading, but doesn't specify API provider availability.

**Solution Options**:
1. **Defer plugin script initialization** until after API provider is created
2. **Create API provider earlier** (before SystemManager.Initialize())
3. **Initialize plugin scripts in two phases**: Load/compile in PreloadAllScripts(), initialize after API provider ready

**Recommended**: Option 3 - Load and compile plugin scripts in `PreloadAllScripts()`, but defer initialization until API provider is available. Add `InitializePluginScripts(IScriptApiProvider)` method.

**Required Change**:
- Split plugin script loading from initialization
- Add `InitializePluginScripts(IScriptApiProvider)` method
- Call initialization after API provider is created

---

### 4. **Parameter Override Resolution**

**Issue**: Plan's code snippet shows using `attachment.ParameterOverrides` which doesn't exist in the component.

**Problem**:
- `ScriptAttachmentComponent` is pure value type (no `ParameterOverrides` field)
- Design specifies parameter overrides stored in `EntityVariablesComponent`
- Plan's `BuildScriptParameters()` method signature is incorrect

**Design Reference**: Design shows parameters checked from `EntityVariablesComponent` with key format `"script:{ScriptDefinitionId}:param:{ParamName}"`.

**Solution**:
- `BuildScriptParameters()` should take `Entity` and `ScriptDefinitionId`
- Check `EntityVariablesComponent` for overrides
- Fall back to definition defaults

**Required Change**:
```csharp
private Dictionary<string, object> BuildScriptParameters(
    Entity entity,
    ScriptDefinition scriptDef)
{
    var parameters = new Dictionary<string, object>();
    
    // Start with definition defaults
    // ...
    
    // Check EntityVariablesComponent for overrides
    if (World.Has<EntityVariablesComponent>(entity))
    {
        var vars = World.Get<EntityVariablesComponent>(entity);
        foreach (var paramDef in scriptDef.Parameters)
        {
            var overrideKey = $"script:{scriptDef.Id}:param:{paramDef.Name}";
            if (vars.Variables.TryGetValue(overrideKey, out var overrideValue))
            {
                parameters[paramDef.Name] = overrideValue;
            }
        }
    }
    
    return parameters;
}
```

---

### 5. **ScriptContext Constructor Missing Parameters**

**Issue**: Plan doesn't specify all required parameters for `ScriptContext` constructor.

**Problem**:
- Design shows `ScriptContext` needs: `world`, `entity`, `logger`, `apis`, `scriptDefinitionId`, `parameters`
- Plan's code snippet shows some parameters but not `scriptDefinitionId`
- Constructor signature not fully specified

**Solution**:
- Document full constructor signature in plan
- Ensure all parameters are passed in `InitializeScript()`

**Required Change**:
```csharp
var context = new ScriptContext(
    world: World,
    entity: entity,
    logger: _logger,
    apis: _apiProvider,
    scriptDefinitionId: attachment.ScriptDefinitionId,
    parameters: parameters
);
```

---

### 6. **PreloadAllScripts() Timing**

**Issue**: Plan says to call `PreloadAllScripts()` "after mod loading completes", but timing is ambiguous.

**Problem**:
- Mods load in `LoadModsSynchronously()` (before SystemManager creation)
- SystemManager.Initialize() is called later
- When exactly should PreloadAllScripts() be called?

**Current Flow**:
1. `LoadModsSynchronously()` - loads mods and definitions
2. SystemManager created
3. `SystemManager.Initialize()` called

**Solution**:
- Call `PreloadAllScripts()` in `SystemManager.Initialize()` before creating systems
- Or: Call in `GameInitializationHelper.InitializeEcsSystems()` after mods load but before SystemManager.Initialize()

**Recommended**: Call in `SystemManager.Initialize()` at the start, after core services are initialized but before systems are created.

**Required Change**:
- Specify exact location: Start of `SystemManager.Initialize()`, after `InitializeCoreServices()`
- Ensure ScriptLoaderService is created before this call

---

### 7. **Error Handling Location**

**Issue**: Plan mentions wrapping event handlers in try-catch but doesn't specify where.

**Problem**:
- Should errors be caught in `EventSubscription` wrapper?
- Or in `ScriptLifecycleSystem` when calling handlers?
- Or both?

**Design Reference**: Design mentions wrapping handlers and firing `ScriptErrorEvent`.

**Solution**:
- Wrap in `EventSubscription` wrapper when invoking handlers
- Fire `ScriptErrorEvent` on exceptions
- Log errors
- Don't crash game

**Required Change**:
- Add error handling to `EventSubscription` wrapper
- Wrap handler invocation in try-catch
- Fire `ScriptErrorEvent` on exception

---

### 8. **ScriptLoaderService Dependencies**

**Issue**: Plan says ScriptLoaderService needs `IModManager`, but should verify interface exists.

**Problem**:
- Need to check if `ModManager` implements `IModManager` interface
- Or if we should use `ModLoader` directly
- Or if we need to create the interface

**Solution**:
- Check if `IModManager` exists
- If not, use `ModManager` directly or create interface
- `ModLoader` is separate from `ModManager` - need to clarify which to use

**Required Change**:
- Verify `IModManager` interface exists
- If not, use `ModManager` directly or specify interface creation
- Clarify relationship between ModLoader, ModManager, and ScriptLoaderService

---

### 9. **Hot-Reload Implementation Missing**

**Issue**: Design mentions hot-reload but plan doesn't detail implementation.

**Problem**:
- Hot-reload is mentioned in design as a feature
- Plan doesn't specify how to implement it
- No file watcher, recompilation, or state preservation details

**Design Reference**: Design shows hot-reload flow but implementation details are missing.

**Solution**:
- Add hot-reload as future phase (Phase 7)
- Or document as "out of scope for initial implementation"
- Or add basic file watcher in ScriptLoaderService

**Required Change**:
- Either add hot-reload implementation details
- Or mark as "future enhancement" and remove from Phase 3

---

### 10. **Script Definition Loading Pattern**

**Issue**: Plan says to load from `Definitions/Scripts/` but doesn't specify the exact pattern.

**Problem**:
- Need to match pattern used by other definitions (e.g., ShaderDefinition)
- Need to know subdirectory structure
- Need to know JSON file naming convention

**Solution**:
- Check how other definitions are loaded in ModLoader
- Match the pattern (likely recursive directory scan)
- Register with type "Script" in DefinitionRegistry

**Required Change**:
- Specify exact loading pattern matching other definitions
- Document subdirectory structure expected

---

## Missing Dependencies

### 11. **ScriptLifecycleSystem Needs DefinitionRegistry**

**Issue**: Plan shows ScriptLifecycleSystem getting script definition, but doesn't specify how.

**Problem**:
- `InitializeScript()` needs to get `ScriptDefinition` from DefinitionRegistry
- Plan doesn't show DefinitionRegistry as dependency
- Need to pass DefinitionRegistry or access via ModManager

**Solution**:
- Add `DefinitionRegistry` as dependency to ScriptLifecycleSystem
- Or access via `ModManager.Registry`
- Get script definition in `InitializeScript()`

**Required Change**:
- Add DefinitionRegistry access to ScriptLifecycleSystem
- Update constructor dependencies

---

### 12. **ScriptContext Needs World Reference**

**Issue**: Plan shows ScriptContext with private World field, but doesn't show how entity operations work.

**Problem**:
- `CreateEntity()`, `DestroyEntity()`, `Query()` need World access
- World is passed to constructor but not stored
- Need to verify World is stored as private field

**Solution**:
- Ensure World is stored as private field in ScriptContext
- Entity operations delegate to World
- Document this in plan

**Required Change**:
- Verify ScriptContext stores World as private field
- Document entity operation implementation

---

## Implementation Order Issues

### 13. **Phase 3 Depends on Phase 4**

**Issue**: ScriptLifecycleSystem needs IScriptApiProvider, but API provider is created in Phase 4.

**Problem**:
- Phase 3 creates ScriptLifecycleSystem which needs IScriptApiProvider
- Phase 4 creates IScriptApiProvider
- Circular dependency or wrong order

**Solution**:
- Create IScriptApiProvider and basic implementation in Phase 3
- Or: Create ScriptLifecycleSystem without API provider, add it later
- Or: Reorder phases so API provider is created first

**Recommended**: Create basic IScriptApiProvider stub in Phase 3, full implementation in Phase 4.

**Required Change**:
- Add IScriptApiProvider stub creation to Phase 3
- Or reorder phases

---

## Summary of Required Changes

### Critical (Must Fix Before Implementation)

1. ✅ **Script Instance Creation**: Change ScriptLoaderService to provide factories/types, not instances
2. ✅ **EntityDestroyedEvent**: Create event (required, not optional)
3. ✅ **Parameter Override Resolution**: Fix BuildScriptParameters to use EntityVariablesComponent
4. ✅ **ScriptContext Constructor**: Document full parameter list including scriptDefinitionId
5. ✅ **PreloadAllScripts() Timing**: Specify exact call location in SystemManager.Initialize()
6. ✅ **Plugin Script Initialization**: Split loading from initialization, defer until API provider ready

### Important (Should Fix)

7. ⚠️ **Error Handling**: Specify error handling location (EventSubscription wrapper)
8. ⚠️ **ScriptLoaderService Dependencies**: Verify IModManager interface or use ModManager directly
9. ⚠️ **ScriptLifecycleSystem Dependencies**: Add DefinitionRegistry access
10. ⚠️ **Phase Ordering**: Resolve Phase 3/4 dependency (API provider)

### Future Enhancements

11. 🔮 **Hot-Reload**: Add implementation details or mark as future enhancement
12. 🔮 **Script Definition Loading Pattern**: Document exact pattern matching other definitions

---

## Recommended Plan Updates

1. **Update Phase 1**: Clarify script instance creation (factory pattern)
2. **Update Phase 2**: Split plugin script loading from initialization
3. **Update Phase 3**: 
   - Add DefinitionRegistry dependency
   - Create IScriptApiProvider stub
   - Specify PreloadAllScripts() call location
4. **Update Phase 4**: Full API provider implementation
5. **Update Phase 6**: Make EntityDestroyedEvent required (not optional)
6. **Add Error Handling**: Specify try-catch in EventSubscription wrapper
7. **Add Parameter Building**: Fix to use EntityVariablesComponent


