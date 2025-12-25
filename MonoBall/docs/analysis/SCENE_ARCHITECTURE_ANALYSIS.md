# Scene/System Architecture Analysis

## Executive Summary

The current scene architecture mixes paradigms (ECS entities + OOP classes), creating architectural inconsistencies and maintenance challenges. This document analyzes the issues and proposes industry-standard solutions.

---

## 🔴 Current Architecture Issues

### 1. **Dual Representation Problem**
**Issue:** Scenes exist as both:
- ECS entities with components (data)
- C# classes with behavior (Scene, GameScene, LoadingScene, etc.)

**Problem:**
- `SceneInstanceComponent` stores a reference to a `Scene` class instance
- This breaks ECS purity - components should be pure data (structs)
- Creates two sources of truth for scene state
- Makes serialization/persistence difficult
- Violates ECS principle: "Components are data, Systems are behavior"

**Current Code:**
```csharp
// Scene is an ECS entity
Entity sceneEntity = World.Create(sceneComponent);

// But also a class instance stored in a component
struct SceneInstanceComponent {
    public Scene Scene { get; set; } // ❌ Reference type in component!
}
```

### 2. **Split Responsibilities**
**Issue:** Scene lifecycle is split across multiple systems:
- `SceneManagerSystem` - handles lifecycle, updates
- `SceneRendererSystem` - handles rendering
- `SceneInputSystem` - handles input

**Problem:**
- Update and Render logic separated unnecessarily
- Scene iteration logic duplicated
- Harder to reason about scene flow
- More systems to coordinate

### 3. **Factory Function Pattern**
**Issue:** Factory functions passed around to create scene instances:
```csharp
Func<Entity, GameScene> createGameScene = ...;
system.RegisterSceneFactory(createGameScene);
```

**Problem:**
- Adds complexity
- Hard to track dependencies
- Not idiomatic C#/ECS
- Creates tight coupling between systems

### 4. **Scene Classes Have Too Many Dependencies**
**Issue:** Scene classes directly depend on rendering systems:
```csharp
public class GameScene : Scene {
    private readonly MapRendererSystem _mapRendererSystem;
    private readonly SpriteRendererSystem _spriteRendererSystem;
    // ... many more dependencies
}
```

**Problem:**
- Violates Dependency Inversion Principle
- Hard to test (many mocks needed)
- Tight coupling to implementation details
- Scenes shouldn't know about specific renderers

### 5. **Mixed Paradigms**
**Issue:** Combining OOP (Scene classes) with ECS (entities/components/systems)

**Problem:**
- Unclear ownership model
- Breaks ECS principles
- Makes it harder to leverage ECS benefits (querying, filtering, etc.)
- Inconsistent with rest of codebase (which is ECS-based)

---

## 🎮 Industry Best Practices

### Unity's Approach
- **Scenes:** Container objects (data)
- **MonoBehaviours:** Behavior components attached to GameObjects
- **Systems:** Unity's ECS (DOTS) uses pure ECS for performance-critical code
- **Pattern:** Scenes own GameObjects, GameObjects have Components, Components have behavior

### Unreal Engine's Approach
- **Levels:** Container objects (data)
- **Actors:** Behavior objects placed in levels
- **Components:** Reusable behavior modules
- **Pattern:** Levels own Actors, Actors have Components, Components provide behavior

### Godot's Approach
- **Scenes:** Node trees (data structure)
- **Scripts:** Behavior attached to nodes
- **Pattern:** Scenes are hierarchical data, scripts provide behavior

### ECS-First Engines (Flecs, EnTT, Arch)
- **Everything is data:** Entities and components only
- **Systems provide behavior:** Query entities, process components
- **No classes:** Pure data-driven architecture
- **Pattern:** Systems query entities by component composition

---

## ✅ Recommended Architecture

### Option 1: Pure ECS (Recommended for MonoBall)

**Principle:** Scenes are just entities with components. Systems handle all behavior.

#### Structure:
```
SceneEntity (Entity)
├── SceneComponent (data: SceneId, Priority, CameraMode, etc.)
├── GameSceneComponent (marker)
└── SceneStateComponent (data: IsActive, IsPaused, etc.)

SceneSystem (System)
├── Update() - queries SceneComponent, calls update logic
└── Render() - queries SceneComponent, calls render logic

SceneUpdateSystem (System)
└── Updates scenes based on type (GameScene, LoadingScene, etc.)

SceneRenderSystem (System)
└── Renders scenes based on type and camera mode
```

#### Benefits:
- ✅ Pure ECS - consistent with rest of codebase
- ✅ Queryable - can query for active scenes, paused scenes, etc.
- ✅ Testable - systems can be tested independently
- ✅ Extensible - add new scene types by adding components
- ✅ No dual representation
- ✅ Leverages ECS performance benefits

#### Implementation:
```csharp
// Scene is just an entity with components
Entity gameScene = World.Create(
    new SceneComponent { SceneId = "game:main", ... },
    new GameSceneComponent()
);

// System handles behavior
public class SceneSystem : BaseSystem<World, float> {
    public override void Update(in float deltaTime) {
        // Query for active scenes
        World.Query(in _activeScenesQuery, (Entity e, ref SceneComponent scene) => {
            if (scene.IsActive && !scene.IsPaused) {
                UpdateScene(e, ref scene, deltaTime);
            }
        });
    }
    
    private void UpdateScene(Entity sceneEntity, ref SceneComponent scene, float deltaTime) {
        // Determine scene type and update accordingly
        if (World.Has<GameSceneComponent>(sceneEntity)) {
            UpdateGameScene(sceneEntity, deltaTime);
        } else if (World.Has<LoadingSceneComponent>(sceneEntity)) {
            UpdateLoadingScene(sceneEntity, deltaTime);
        }
    }
}
```

### Option 2: Hybrid (Scenes as Containers)

**Principle:** Scenes are lightweight containers that own their systems/entities.

#### Structure:
```
Scene (class)
├── SceneEntity (Entity) - ECS representation
├── Update(GameTime) - delegates to systems
└── Render(GameTime) - delegates to systems

SceneManager (class)
├── SceneStack (List<Scene>)
├── Update(GameTime) - iterates scenes, calls Update()
└── Render(GameTime) - iterates scenes, calls Render()
```

#### Benefits:
- ✅ Clear ownership
- ✅ Encapsulates scene-specific logic
- ✅ Can still leverage ECS for game entities
- ✅ Familiar OOP pattern

#### Drawbacks:
- ❌ Still mixes paradigms
- ❌ Can't query scenes easily
- ❌ Less flexible than pure ECS

### Option 3: Scene Graph Pattern

**Principle:** Scenes form a tree structure like Godot/Unity.

#### Structure:
```
SceneNode (base class)
├── Children (List<SceneNode>)
├── Update(GameTime)
└── Render(GameTime)

SceneManager
└── RootScene (SceneNode)
```

#### Benefits:
- ✅ Hierarchical organization
- ✅ Parent-child relationships
- ✅ Familiar to Unity/Godot developers

#### Drawbacks:
- ❌ Doesn't fit ECS model
- ❌ More complex than needed for current use case
- ❌ Overkill for flat scene stack

---

## 🎯 Recommended Solution: Pure ECS Approach

### Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    SceneSystem                           │
│  ┌──────────────────────────────────────────────────┐  │
│  │ Update()                                          │  │
│  │  - Queries SceneComponent + scene type markers   │  │
│  │  - Calls UpdateScene() based on type             │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────┐  │
│  │ Render()                                         │  │
│  │  - Queries SceneComponent + scene type markers   │  │
│  │  - Calls RenderScene() based on type             │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
                        │
                        ▼
        ┌───────────────────────────────┐
        │   SceneEntity (Entity)        │
        │  ┌─────────────────────────┐ │
        │  │ SceneComponent          │ │
        │  │ - SceneId               │ │
        │  │ - Priority              │ │
        │  │ - CameraMode            │ │
        │  │ - BlocksUpdate/Draw     │ │
        │  │ - IsActive/IsPaused     │ │
        │  └─────────────────────────┘ │
        │  ┌─────────────────────────┐ │
        │  │ GameSceneComponent      │ │ (marker)
        │  └─────────────────────────┘ │
        └───────────────────────────────┘
```

### Key Changes

1. **Remove Scene Classes**
   - Delete `Scene`, `GameScene`, `LoadingScene`, etc.
   - Delete `SceneInstanceComponent`
   - Scenes are just entities with components

2. **Unify SceneSystem**
   - Merge `SceneManagerSystem` and `SceneRendererSystem` into `SceneSystem`
   - `SceneSystem` handles both Update and Render
   - Single source of truth for scene iteration

3. **Type-Based Behavior**
   - Use marker components (`GameSceneComponent`, `LoadingSceneComponent`) to identify scene types
   - `SceneSystem` switches on component presence to determine behavior
   - Each scene type has dedicated update/render methods in the system

4. **Dependency Injection**
   - `SceneSystem` receives all dependencies (renderers, managers, etc.)
   - Scene entities don't store references to systems
   - Systems query for scene entities, not the other way around

### Implementation Example

```csharp
public class SceneSystem : BaseSystem<World, float> {
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SpriteBatch _spriteBatch;
    private readonly MapRendererSystem _mapRendererSystem;
    private readonly ShaderManagerSystem? _shaderManagerSystem;
    
    // Cached queries
    private readonly QueryDescription _activeScenesQuery = new QueryDescription()
        .WithAll<SceneComponent>()
        .WithAny<GameSceneComponent, LoadingSceneComponent, MapPopupSceneComponent>();
    
    public override void Update(in float deltaTime) {
        var gameTime = new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(deltaTime));
        
        World.Query(in _activeScenesQuery, (Entity e, ref SceneComponent scene) => {
            if (!scene.IsActive || scene.IsPaused) return;
            
            // Update based on scene type
            if (World.Has<GameSceneComponent>(e)) {
                UpdateGameScene(e, ref scene, gameTime);
            } else if (World.Has<LoadingSceneComponent>(e)) {
                UpdateLoadingScene(e, ref scene, gameTime);
            }
            // ... other types
            
            // Stop if scene blocks update
            if (scene.BlocksUpdate) return false;
            return true;
        });
    }
    
    public void Render(GameTime gameTime) {
        // Iterate in reverse for rendering (higher priority on top)
        _sceneStack.Reverse();
        foreach (var sceneEntity in _sceneStack) {
            if (!World.IsAlive(sceneEntity)) continue;
            
            ref var scene = ref World.Get<SceneComponent>(sceneEntity);
            if (!scene.IsActive) continue;
            
            // Render based on scene type
            if (World.Has<GameSceneComponent>(sceneEntity)) {
                RenderGameScene(sceneEntity, ref scene, gameTime);
            } else if (World.Has<LoadingSceneComponent>(sceneEntity)) {
                RenderLoadingScene(sceneEntity, ref scene, gameTime);
            }
            // ... other types
            
            if (scene.BlocksDraw) break;
        }
    }
    
    private void UpdateGameScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime) {
        // Game scenes don't need per-frame updates typically
        // But if they do, add logic here
    }
    
    private void RenderGameScene(Entity sceneEntity, ref SceneComponent scene, GameTime gameTime) {
        var camera = GetCameraForScene(ref scene);
        if (!camera.HasValue) return;
        
        // Render maps, sprites, borders, post-processing
        _mapRendererSystem.Render(gameTime, sceneEntity);
        // ... rest of rendering logic
    }
}
```

---

## 📊 Comparison Matrix

| Aspect | Current (Mixed) | Pure ECS | Hybrid | Scene Graph |
|--------|----------------|----------|--------|-------------|
| **Consistency** | ❌ Mixed paradigms | ✅ Pure ECS | ⚠️ Mixed | ⚠️ OOP |
| **Queryability** | ⚠️ Limited | ✅ Full ECS queries | ❌ No queries | ❌ No queries |
| **Performance** | ⚠️ Good | ✅ Excellent | ⚠️ Good | ⚠️ Good |
| **Testability** | ⚠️ Complex | ✅ Simple | ⚠️ Complex | ⚠️ Complex |
| **Maintainability** | ❌ Complex | ✅ Simple | ⚠️ Moderate | ⚠️ Moderate |
| **Extensibility** | ⚠️ Moderate | ✅ High | ⚠️ Moderate | ⚠️ Moderate |
| **Codebase Fit** | ❌ Inconsistent | ✅ Consistent | ⚠️ Mixed | ❌ Different |

---

## 🚀 Migration Path

### Phase 1: Unify Systems
1. Merge `SceneRendererSystem` into `SceneManagerSystem`
2. Rename to `SceneSystem`
3. Add `Render()` method alongside `Update()`

### Phase 2: Remove Scene Classes
1. Move rendering logic from Scene classes into `SceneSystem`
2. Move update logic from Scene classes into `SceneSystem`
3. Delete Scene classes (`Scene`, `GameScene`, etc.)
4. Delete `SceneInstanceComponent`

### Phase 3: Refactor Scene Creation
1. Update `CreateScene()` to not require scene instances
2. Remove factory functions
3. Simplify scene creation code

### Phase 4: Cleanup
1. Remove unused renderer classes
2. Update documentation
3. Update tests

---

## 📝 Code Examples

### Before (Current - Mixed Paradigm)
```csharp
// Create scene entity
var sceneEntity = sceneManager.CreateScene(sceneComponent, new GameSceneComponent());

// Create scene class instance
var gameScene = new GameScene(sceneEntity, world, graphicsDevice, spriteBatch, ...);

// Store class instance in component (breaks ECS!)
sceneManager.SetSceneInstance(sceneEntity, gameScene);

// System calls scene class method
sceneInstanceComponent.Scene.Update(gameTime);
```

### After (Pure ECS)
```csharp
// Create scene entity (that's it!)
var sceneEntity = sceneSystem.CreateScene(sceneComponent, new GameSceneComponent());

// System queries and handles behavior
sceneSystem.Update(deltaTime); // Queries entities, handles updates
sceneSystem.Render(gameTime);  // Queries entities, handles rendering
```

---

## 🎓 Key Principles

1. **ECS Purity:** Components are data, Systems are behavior
2. **Single Responsibility:** One system handles scene lifecycle
3. **Query-Based:** Use ECS queries to find scenes, don't store references
4. **Type Markers:** Use marker components to identify scene types
5. **Dependency Flow:** Systems depend on components, not the other way around

---

## ✅ Benefits of Pure ECS Approach

1. **Consistency:** Matches rest of codebase architecture
2. **Performance:** Leverages ECS query optimizations
3. **Flexibility:** Easy to add new scene types (just add component)
4. **Testability:** Systems can be tested independently
5. **Maintainability:** Single source of truth, clear responsibilities
6. **Extensibility:** Query for scenes by any component combination
7. **Serialization:** Entities/components can be serialized easily

---

## 🔍 Questions to Consider

1. **Do scenes need complex state machines?**
   - If yes, consider `SceneStateComponent` enum
   - Systems handle state transitions

2. **Do scenes need to own entities?**
   - Current: No, scenes are just markers
   - Future: Could add `SceneOwnershipComponent` if needed

3. **Do scenes need lifecycle hooks?**
   - Add events: `SceneCreatedEvent`, `SceneDestroyedEvent`
   - Systems subscribe to events for initialization/cleanup

4. **Performance requirements?**
   - Pure ECS is fastest (cache-friendly queries)
   - Current mixed approach has overhead

---

## 📚 References

- [Arch ECS Documentation](https://github.com/genaray/Arch)
- [ECS Best Practices](https://github.com/SanderMertens/flecs/blob/master/docs/Manual.md)
- Unity DOTS Architecture
- Unreal Engine ECS (Mass Entity)
- [Game Programming Patterns - Component](https://gameprogrammingpatterns.com/component.html)

