# Architecture Issues Analysis

**Date:** 2025-01-XX  
**Scope:** SceneSystem refactoring and related architectural patterns  
**Status:** 🔴 CRITICAL ISSUES IDENTIFIED

---

## Executive Summary

The recent refactoring consolidated scene-specific systems but introduced several architectural violations:

1. **🔴 CRITICAL: SceneSystem Constructor Bloat** - 13+ parameters violates SRP and DIP
2. **🔴 CRITICAL: Circular Dependency** - MapPopupSceneSystem ↔ SceneSystem
3. **🟡 HIGH: SceneSystem Creating Systems** - Violates SRP, should only manage lifecycle
4. **🟡 HIGH: Tight Coupling** - SceneSystem knows all scene-specific system types
5. **🟡 HIGH: Inconsistent Update/Render Pattern** - Update() calls systems directly, Render() iterates scenes
6. **🟡 MEDIUM: Missing Interface Abstraction** - No ISceneSystem interface
7. **🟡 MEDIUM: Optional Parameters** - Many nullable parameters suggest incomplete initialization
8. **🟢 LOW: DRY Violations** - Repeated pattern matching in Update/Render

---

## 1. 🔴 CRITICAL: SceneSystem Constructor Bloat

### Problem

```csharp
public SceneSystem(
    World world,
    ILogger logger,
    GraphicsDevice graphicsDevice,
    SpriteBatch spriteBatch,
    Game game,                                    // For LoadingSceneSystem
    MapRendererSystem mapRendererSystem,          // For GameSceneSystem
    SpriteRendererSystem? spriteRendererSystem,   // For GameSceneSystem
    MapBorderRendererSystem? mapBorderRendererSystem, // For GameSceneSystem
    ShaderManagerSystem? shaderManagerSystem,     // For rendering
    ShaderRendererSystem? shaderRendererSystem,   // For GameSceneSystem
    RenderTargetManager? renderTargetManager,     // For GameSceneSystem
    FontService? fontService,                     // For DebugBarSceneSystem, MapPopupSceneSystem
    PerformanceStatsSystem? performanceStatsSystem, // For DebugBarSceneSystem
    IModManager? modManager                       // For MapPopupSceneSystem
)
```

**Issues:**
- ❌ **13+ constructor parameters** - Violates "God Constructor" anti-pattern
- ❌ **Violates Single Responsibility Principle** - SceneSystem shouldn't know about all scene-specific system dependencies
- ❌ **Violates Dependency Inversion Principle** - Depends on concrete types, not abstractions
- ❌ **Hard to test** - Requires mocking 13+ dependencies
- ❌ **Hard to maintain** - Adding a new scene type requires modifying SceneSystem constructor
- ❌ **Tight coupling** - SceneSystem knows about LoadingSceneSystem, GameSceneSystem, DebugBarSceneSystem, MapPopupSceneSystem internals

### Root Cause

SceneSystem is trying to do too much:
1. Manage scene lifecycle (its responsibility)
2. Create scene-specific systems (should be SystemManager's responsibility)
3. Coordinate updates/renders (its responsibility, but wrong pattern)

### Solution Options

#### Option A: Factory Pattern (Recommended)
```csharp
public interface ISceneSystemFactory
{
    GameSceneSystem CreateGameSceneSystem();
    LoadingSceneSystem CreateLoadingSceneSystem();
    DebugBarSceneSystem CreateDebugBarSceneSystem();
    MapPopupSceneSystem CreateMapPopupSceneSystem(SceneSystem sceneSystem);
}

public class SceneSystem : BaseSystem<World, float>
{
    private readonly ISceneSystemFactory _sceneSystemFactory;
    
    public SceneSystem(
        World world,
        ILogger logger,
        GraphicsDevice graphicsDevice,
        ShaderManagerSystem? shaderManagerSystem,
        ISceneSystemFactory sceneSystemFactory  // Single dependency
    ) : base(world)
    {
        _sceneSystemFactory = sceneSystemFactory ?? throw new ArgumentNullException(nameof(sceneSystemFactory));
        
        // Create scene-specific systems via factory
        _gameSceneSystem = _sceneSystemFactory.CreateGameSceneSystem();
        _loadingSceneSystem = _sceneSystemFactory.CreateLoadingSceneSystem();
        _debugBarSceneSystem = _sceneSystemFactory.CreateDebugBarSceneSystem();
        _mapPopupSceneSystem = _sceneSystemFactory.CreateMapPopupSceneSystem(this);
    }
}
```

#### Option B: SystemManager Creates, SceneSystem Receives
```csharp
// SystemManager creates all scene-specific systems
// SceneSystem receives them as constructor parameters
public SceneSystem(
    World world,
    ILogger logger,
    GraphicsDevice graphicsDevice,
    ShaderManagerSystem? shaderManagerSystem,
    GameSceneSystem gameSceneSystem,
    LoadingSceneSystem loadingSceneSystem,
    DebugBarSceneSystem debugBarSceneSystem,
    MapPopupSceneSystem mapPopupSceneSystem
)
```

**Trade-offs:**
- ✅ SceneSystem doesn't create systems
- ❌ Still has many parameters (but they're the systems themselves, not their dependencies)
- ❌ SystemManager still needs to know about all scene-specific systems

#### Option C: Registry Pattern
```csharp
public interface ISceneSystemRegistry
{
    void Register<T>(T system) where T : ISceneSystem;
    T? Get<T>() where T : ISceneSystem;
}

public class SceneSystem : BaseSystem<World, float>
{
    private readonly ISceneSystemRegistry _registry;
    
    public SceneSystem(
        World world,
        ILogger logger,
        GraphicsDevice graphicsDevice,
        ShaderManagerSystem? shaderManagerSystem,
        ISceneSystemRegistry registry
    )
    {
        _registry = registry;
        _gameSceneSystem = _registry.Get<GameSceneSystem>();
        // ...
    }
}
```

---

## 2. 🔴 CRITICAL: Circular Dependency

### Problem

```csharp
// SceneSystem.cs
_mapPopupSceneSystem = new MapPopupSceneSystem(
    world,
    this, // ❌ Passes self-reference
    // ...
);

// MapPopupSceneSystem.cs
public MapPopupSceneSystem(
    World world,
    SceneSystem sceneSystem, // ❌ Needs SceneSystem
    // ...
)
```

**Issues:**
- ❌ **Circular dependency** - SceneSystem creates MapPopupSceneSystem, MapPopupSceneSystem needs SceneSystem
- ❌ **Tight coupling** - MapPopupSceneSystem directly depends on SceneSystem concrete type
- ❌ **Hard to test** - Can't test MapPopupSceneSystem without SceneSystem
- ❌ **Violates Dependency Inversion Principle** - Depends on concrete type

### Why MapPopupSceneSystem Needs SceneSystem

Looking at MapPopupSceneSystem, it likely needs SceneSystem to:
- Create/destroy scenes
- Query scene state
- Fire scene events

### Solution Options

#### Option A: Interface Abstraction (Recommended)
```csharp
public interface ISceneManager
{
    Entity CreateScene(SceneComponent sceneComponent, params Component[] additionalComponents);
    void DestroyScene(string sceneId);
    Entity? GetSceneEntity(string sceneId);
    bool IsSceneActive(string sceneId);
}

public class SceneSystem : BaseSystem<World, float>, ISceneManager
{
    // Implements ISceneManager
}

public class MapPopupSceneSystem : BaseSystem<World, float>
{
    private readonly ISceneManager _sceneManager; // ✅ Depends on interface
    
    public MapPopupSceneSystem(
        World world,
        ISceneManager sceneManager, // ✅ Interface, not concrete type
        // ...
    )
}
```

#### Option B: Event-Based Communication
```csharp
// MapPopupSceneSystem doesn't need SceneSystem reference
// Instead, fires events that SceneSystem subscribes to

public struct CreateSceneEvent
{
    public SceneComponent SceneComponent;
    public Component[] AdditionalComponents;
}

// MapPopupSceneSystem fires event
EventBus.Send(ref new CreateSceneEvent { ... });

// SceneSystem subscribes and handles
EventBus.Subscribe<CreateSceneEvent>(OnCreateScene);
```

**Trade-offs:**
- ✅ No circular dependency
- ✅ Loose coupling
- ❌ Less direct control
- ❌ Async event handling complexity

---

## 3. 🟡 HIGH: SceneSystem Creating Systems

### Problem

SceneSystem is responsible for:
1. ✅ Managing scene lifecycle (its responsibility)
2. ❌ Creating scene-specific systems (should be SystemManager's responsibility)
3. ✅ Coordinating updates/renders (its responsibility)

**Violates Single Responsibility Principle** - SceneSystem has multiple reasons to change:
- Changes when scene lifecycle logic changes
- Changes when scene-specific system creation logic changes
- Changes when new scene types are added

### Current Pattern

```csharp
public class SceneSystem
{
    public SceneSystem(/* 13+ parameters */)
    {
        // ❌ Creates LoadingSceneSystem
        _loadingSceneSystem = new LoadingSceneSystem(...);
        
        // ❌ Creates GameSceneSystem
        _gameSceneSystem = new GameSceneSystem(...);
        
        // ❌ Creates DebugBarSceneSystem
        _debugBarSceneSystem = new DebugBarSceneSystem(...);
        
        // ❌ Creates MapPopupSceneSystem
        _mapPopupSceneSystem = new MapPopupSceneSystem(...);
    }
}
```

### Solution

**SystemManager should create scene-specific systems, SceneSystem should receive them:**

```csharp
// SystemManager.cs
private void CreateSceneSpecificSystems()
{
    // Create scene-specific systems
    var gameSceneSystem = new GameSceneSystem(...);
    var loadingSceneSystem = new LoadingSceneSystem(...);
    var debugBarSceneSystem = new DebugBarSceneSystem(...);
    var mapPopupSceneSystem = new MapPopupSceneSystem(...);
    
    // Create SceneSystem and pass systems to it
    _sceneSystem = new SceneSystem(
        _world,
        logger,
        graphicsDevice,
        shaderManagerSystem,
        gameSceneSystem,
        loadingSceneSystem,
        debugBarSceneSystem,
        mapPopupSceneSystem
    );
}

// SceneSystem.cs
public SceneSystem(
    World world,
    ILogger logger,
    GraphicsDevice graphicsDevice,
    ShaderManagerSystem? shaderManagerSystem,
    GameSceneSystem gameSceneSystem,
    LoadingSceneSystem loadingSceneSystem,
    DebugBarSceneSystem debugBarSceneSystem,
    MapPopupSceneSystem mapPopupSceneSystem
)
{
    _gameSceneSystem = gameSceneSystem;
    _loadingSceneSystem = loadingSceneSystem;
    _debugBarSceneSystem = debugBarSceneSystem;
    _mapPopupSceneSystem = mapPopupSceneSystem;
}
```

**Benefits:**
- ✅ SceneSystem only manages lifecycle and coordination
- ✅ SystemManager owns system creation (its responsibility)
- ✅ Easier to test SceneSystem (can inject mock systems)
- ✅ Clear separation of concerns

---

## 4. 🟡 HIGH: Tight Coupling

### Problem

SceneSystem knows about all scene-specific system types:

```csharp
private readonly GameSceneSystem? _gameSceneSystem;
private readonly LoadingSceneSystem? _loadingSceneSystem;
private readonly DebugBarSceneSystem? _debugBarSceneSystem;
private readonly MapPopupSceneSystem? _mapPopupSceneSystem;
```

**Issues:**
- ❌ **Violates Open/Closed Principle** - Adding a new scene type requires modifying SceneSystem
- ❌ **Can't mod scenes** - Mods can't add new scene types without modifying core code
- ❌ **Tight coupling** - SceneSystem depends on concrete scene system types

### Solution: Interface Abstraction

```csharp
public interface ISceneSystem
{
    void Update(Entity sceneEntity, float deltaTime);
    void RenderScene(Entity sceneEntity, GameTime gameTime);
}

public class GameSceneSystem : BaseSystem<World, float>, ISceneSystem
{
    public void Update(Entity sceneEntity, float deltaTime) { /* ... */ }
    public void RenderScene(Entity sceneEntity, GameTime gameTime) { /* ... */ }
}

public class SceneSystem : BaseSystem<World, float>
{
    private readonly Dictionary<Type, ISceneSystem> _sceneSystems = new();
    
    public void RegisterSceneSystem<T>(ISceneSystem system) where T : Component
    {
        _sceneSystems[typeof(T)] = system;
    }
    
    public override void Update(in float deltaTime)
    {
        IterateScenes((sceneEntity, sceneComponent) =>
        {
            if (!sceneComponent.IsActive || sceneComponent.IsPaused)
                return true;
            
            // Find appropriate scene system based on component type
            var sceneSystem = FindSceneSystem(sceneEntity);
            sceneSystem?.Update(sceneEntity, deltaTime);
            
            return true;
        });
    }
    
    private ISceneSystem? FindSceneSystem(Entity sceneEntity)
    {
        if (World.Has<GameSceneComponent>(sceneEntity))
            return _sceneSystems.GetValueOrDefault(typeof(GameSceneComponent));
        if (World.Has<LoadingSceneComponent>(sceneEntity))
            return _sceneSystems.GetValueOrDefault(typeof(LoadingSceneComponent));
        // ... etc
    }
}
```

**Benefits:**
- ✅ SceneSystem doesn't know about concrete scene system types
- ✅ Can register scene systems dynamically
- ✅ Mods can add new scene types
- ✅ Follows Open/Closed Principle

---

## 5. 🟡 HIGH: Inconsistent Update/Render Pattern

### Problem

**Update() pattern:**
```csharp
public override void Update(in float deltaTime)
{
    if (isBlocked)
    {
        _loadingSceneSystem?.Update(in deltaTime); // ❌ Calls system.Update() directly
    }
    else
    {
        _gameSceneSystem?.Update(in deltaTime);    // ❌ Calls system.Update() directly
        _loadingSceneSystem?.Update(in deltaTime);
        _debugBarSceneSystem?.Update(in deltaTime);
        _mapPopupSceneSystem?.Update(in deltaTime);
    }
}
```

**Render() pattern:**
```csharp
public void Render(GameTime gameTime)
{
    IterateScenesReverse((sceneEntity, sceneComponent) =>
    {
        if (World.Has<GameSceneComponent>(sceneEntity))
        {
            _gameSceneSystem?.RenderScene(sceneEntity, gameTime); // ✅ Iterates scenes, calls RenderScene()
        }
        // ...
    });
}
```

**Issues:**
- ❌ **Inconsistent** - Update() calls systems directly, Render() iterates scenes
- ❌ **Update() doesn't iterate scenes** - Calls all systems regardless of active scenes
- ❌ **Render() iterates scenes** - Only renders active scenes (correct pattern)

### Solution: Consistent Pattern

Both Update() and Render() should iterate scenes:

```csharp
public override void Update(in float deltaTime)
{
    CleanupDeadEntities();
    
    bool isBlocked = IsUpdateBlocked();
    
    IterateScenes((sceneEntity, sceneComponent) =>
    {
        if (!sceneComponent.IsActive || sceneComponent.IsPaused)
            return true;
        
        if (isBlocked && !World.Has<LoadingSceneComponent>(sceneEntity))
            return true; // Skip non-loading scenes when blocked
        
        // Find appropriate scene system
        var sceneSystem = FindSceneSystem(sceneEntity);
        sceneSystem?.Update(sceneEntity, deltaTime);
        
        return true;
    });
}

public void Render(GameTime gameTime)
{
    IterateScenesReverse((sceneEntity, sceneComponent) =>
    {
        if (!sceneComponent.IsActive)
            return true;
        
        var sceneSystem = FindSceneSystem(sceneEntity);
        sceneSystem?.RenderScene(sceneEntity, gameTime);
        
        return true;
    });
}
```

**Benefits:**
- ✅ Consistent pattern
- ✅ Update() only updates active scenes (correct)
- ✅ Both methods iterate scenes (correct)

---

## 6. 🟡 MEDIUM: Missing Interface Abstraction

### Problem

No interface for scene-specific systems:

```csharp
// ❌ No interface - direct concrete types
private readonly GameSceneSystem? _gameSceneSystem;
private readonly LoadingSceneSystem? _loadingSceneSystem;
```

**Issues:**
- ❌ Can't swap implementations
- ❌ Hard to test (can't inject mocks)
- ❌ Tight coupling

### Solution

```csharp
public interface ISceneSystem
{
    void Update(Entity sceneEntity, float deltaTime);
    void RenderScene(Entity sceneEntity, GameTime gameTime);
}

public class GameSceneSystem : BaseSystem<World, float>, ISceneSystem
{
    public void Update(Entity sceneEntity, float deltaTime) { /* ... */ }
    public void RenderScene(Entity sceneEntity, GameTime gameTime) { /* ... */ }
}
```

---

## 7. 🟡 MEDIUM: Optional Parameters

### Problem

Many nullable parameters suggest incomplete initialization:

```csharp
SpriteRendererSystem? spriteRendererSystem = null,
MapBorderRendererSystem? mapBorderRendererSystem = null,
ShaderManagerSystem? shaderManagerSystem = null,
ShaderRendererSystem? shaderRendererSystem = null,
RenderTargetManager? renderTargetManager = null,
FontService? fontService = null,
PerformanceStatsSystem? performanceStatsSystem = null,
IModManager? modManager = null
```

**Issues:**
- ❌ **Nullable reference types** suggest optional dependencies, but some are required
- ❌ **Runtime null checks** instead of compile-time guarantees
- ❌ **Unclear requirements** - Which parameters are required vs optional?

### Solution

**Make required dependencies non-nullable:**

```csharp
public SceneSystem(
    World world,
    ILogger logger,
    GraphicsDevice graphicsDevice,
    SpriteBatch spriteBatch,
    Game game,                                    // Required
    MapRendererSystem mapRendererSystem,          // Required
    FontService fontService,                      // Required (not optional)
    PerformanceStatsSystem performanceStatsSystem, // Required (not optional)
    IModManager modManager,                       // Required (not optional)
    // Optional dependencies
    SpriteRendererSystem? spriteRendererSystem = null,
    MapBorderRendererSystem? mapBorderRendererSystem = null,
    ShaderManagerSystem? shaderManagerSystem = null,
    ShaderRendererSystem? shaderRendererSystem = null,
    RenderTargetManager? renderTargetManager = null
)
```

**Or use a builder pattern:**

```csharp
public class SceneSystemBuilder
{
    public SceneSystemBuilder WithRequired(/* required params */) { /* ... */ }
    public SceneSystemBuilder WithOptional(/* optional params */) { /* ... */ }
    public SceneSystem Build() { /* ... */ }
}
```

---

## 8. 🟢 LOW: DRY Violations

### Problem

Repeated pattern matching in Update() and Render():

```csharp
// Update()
if (World.Has<GameSceneComponent>(sceneEntity))
    _gameSceneSystem?.Update(sceneEntity, deltaTime);
else if (World.Has<LoadingSceneComponent>(sceneEntity))
    _loadingSceneSystem?.Update(sceneEntity, deltaTime);
// ... repeated pattern

// Render()
if (World.Has<GameSceneComponent>(sceneEntity))
    _gameSceneSystem?.RenderScene(sceneEntity, gameTime);
else if (World.Has<LoadingSceneComponent>(sceneEntity))
    _loadingSceneSystem?.RenderScene(sceneEntity, gameTime);
// ... repeated pattern
```

### Solution

Use a dictionary/registry:

```csharp
private readonly Dictionary<Type, ISceneSystem> _sceneSystems = new();

private ISceneSystem? FindSceneSystem(Entity sceneEntity)
{
    if (World.Has<GameSceneComponent>(sceneEntity))
        return _sceneSystems.GetValueOrDefault(typeof(GameSceneComponent));
    if (World.Has<LoadingSceneComponent>(sceneEntity))
        return _sceneSystems.GetValueOrDefault(typeof(LoadingSceneComponent));
    // ...
    return null;
}
```

---

## 9. Arch ECS Issues

### Problem: SceneSystem.Update() Doesn't Use Queries Properly

**Current:**
```csharp
public override void Update(in float deltaTime)
{
    // ❌ Doesn't query for scenes - uses cached _sceneStack
    // ❌ Calls systems directly instead of querying
    _gameSceneSystem?.Update(in deltaTime);
}
```

**Should be:**
```csharp
public override void Update(in float deltaTime)
{
    // ✅ Query for active scenes
    World.Query(in _activeScenesQuery, (Entity e, ref SceneComponent scene) =>
    {
        if (scene.IsActive && !scene.IsPaused)
        {
            var sceneSystem = FindSceneSystem(e);
            sceneSystem?.Update(e, deltaTime);
        }
    });
}
```

**However**, SceneSystem needs to maintain `_sceneStack` for priority ordering, so caching is acceptable. But Update() should still iterate scenes, not call systems directly.

---

## 10. Event System Issues

### Problem: MapPopupSceneSystem Needs SceneSystem

Instead of direct dependency, use events:

```csharp
// MapPopupSceneSystem fires event
public void ShowPopup(string mapId)
{
    var showEvent = new MapPopupShowEvent { MapId = mapId };
    EventBus.Send(ref showEvent);
}

// SceneSystem subscribes and creates scene
EventBus.Subscribe<MapPopupShowEvent>(OnMapPopupShow);

private void OnMapPopupShow(ref MapPopupShowEvent evt)
{
    var sceneComponent = new SceneComponent { /* ... */ };
    var popupComponent = new MapPopupSceneComponent { MapId = evt.MapId };
    CreateScene(sceneComponent, popupComponent);
}
```

**Benefits:**
- ✅ No circular dependency
- ✅ Loose coupling
- ✅ Event-driven architecture

---

## Recommended Refactoring Plan

### Phase 1: Fix Critical Issues
1. ✅ **Create ISceneManager interface** - Break circular dependency
2. ✅ **Move system creation to SystemManager** - Fix SRP violation
3. ✅ **Reduce SceneSystem constructor parameters** - Use factory or pass systems directly

### Phase 2: Improve Architecture
4. ✅ **Create ISceneSystem interface** - Enable loose coupling
5. ✅ **Use registry pattern** - Enable moddable scenes
6. ✅ **Consistent Update/Render pattern** - Both iterate scenes

### Phase 3: Polish
7. ✅ **Remove optional parameters** - Make requirements explicit
8. ✅ **DRY improvements** - Use dictionary for scene system lookup
9. ✅ **Event-based communication** - Replace direct dependencies with events

---

## Summary

**Critical Issues:**
- 🔴 SceneSystem constructor bloat (13+ parameters)
- 🔴 Circular dependency (MapPopupSceneSystem ↔ SceneSystem)

**High Priority:**
- 🟡 SceneSystem creating systems (should be SystemManager)
- 🟡 Tight coupling (knows all scene system types)
- 🟡 Inconsistent Update/Render pattern

**Medium Priority:**
- 🟡 Missing interface abstraction
- 🟡 Optional parameters (unclear requirements)

**Low Priority:**
- 🟢 DRY violations (repeated pattern matching)

**Recommended Next Steps:**
1. Create `ISceneManager` interface to break circular dependency
2. Move scene-specific system creation to `SystemManager`
3. Refactor `SceneSystem` to receive systems instead of creating them
4. Create `ISceneSystem` interface for loose coupling
5. Use consistent Update/Render pattern (both iterate scenes)

